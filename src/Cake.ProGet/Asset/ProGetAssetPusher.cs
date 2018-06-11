using System;
using System.IO;
using System.Net;
using System.Net.Http;
using Cake.Core;
using Cake.Core.Diagnostics;
using Cake.Core.IO;

namespace Cake.ProGet.Asset
{
    /// <summary>
    /// Manipulates files in ProGet Asset Directories.
    /// </summary>
    internal sealed class ProGetAssetPusher
    {
        private const int ChunkSize = 5 * 1024 * 1024;

        private readonly ICakeLog _log;
        private readonly ProGetConfiguration _configuration;

        /// <summary>
        /// Initializes a new instance of the <see cref="ProGetAssetPusher"/> class.
        /// </summary>
        /// <param name="log">The Cake log.</param>
        /// <param name="configuration">The ProGet Configuration.</param>
        /// <exception cref="ArgumentNullException">Thrown if environment, log, or config are null.</exception>
        public ProGetAssetPusher(ICakeLog log, ProGetConfiguration configuration)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _configuration = configuration ?? throw new ArgumentException(nameof(configuration));
        }

        /// <summary>
        /// Determines if a given asset is published in the Asset Directory.
        /// </summary>
        /// <param name="assetUri">The URI of the asset.</param>
        /// <returns>True, if the asset is found. False otherwise.</returns>
        public bool DoesAssetExist(string assetUri)
        {
            var client = new HttpClient();
            _configuration.Apply(client);

            var result = client.SendAsync(new HttpRequestMessage(HttpMethod.Head, assetUri)).Result;

            if (result.StatusCode.Equals(HttpStatusCode.Unauthorized) || result.StatusCode.Equals(HttpStatusCode.Forbidden))
            {
                throw new CakeException("Authorization to ProGet server failed; Credentials were incorrect, or not supplied.");
            }

            if (result.StatusCode.Equals(HttpStatusCode.OK) || result.StatusCode.Equals(HttpStatusCode.Accepted))
            {
                return true;
            }

            if (result.StatusCode.Equals(HttpStatusCode.NotFound))
            {
                return false;
            }

            throw new CakeException($"An unknown error occurred while checking for asset on the ProGet Server. HTTP {result.StatusCode}");
        }

        /// <summary>
        /// Deletes an asset from the Asset Directory.
        /// </summary>
        /// <param name="assetUri">The URI of the asset.</param>
        /// <returns>True, if the asset is deleted. False otherwise.</returns>
        public bool DeleteAsset(string assetUri)
        {
            var client = new HttpClient();
            _configuration.Apply(client);

            var result = client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, assetUri)).Result;

            if (result.StatusCode.Equals(HttpStatusCode.Unauthorized) || result.StatusCode.Equals(HttpStatusCode.Forbidden))
            {
                throw new CakeException("Authorization to ProGet server failed; Credentials were incorrect, or not supplied.");
            }

            if (result.StatusCode.Equals(HttpStatusCode.OK) || result.StatusCode.Equals(HttpStatusCode.Accepted))
            {
                return true;
            }

            if (result.StatusCode.Equals(HttpStatusCode.BadRequest))
            {
                throw new CakeException("Asset refers to a directory, did not delete.");
            }

            throw new CakeException($"An unknown error occurred while deleting asset on the ProGet Server. HTTP {result.StatusCode}");
        }

        /// <summary>
        /// Publishes an asset to the Asset Directory.
        /// </summary>
        /// <param name="asset">A FilePath to the file to be published.</param>
        /// <param name="uri">The desired URI of the asset.</param>
        public void Publish(FilePath asset, string uri)
        {
            _log.Information($"Publishing {asset} to {uri}...");

            if (new FileInfo(asset.FullPath).Length < ChunkSize)
            {
                var client = new HttpClient();
                _configuration.Apply(client);
                var result = client.PutAsync(new Uri(uri), new StreamContent(File.OpenRead(asset.FullPath))).Result;
                if (result.IsSuccessStatusCode)
                {
                    _log.Information("Upload successful");
                }
                else if (result.StatusCode.Equals(HttpStatusCode.BadRequest))
                {
                    throw new CakeException("Upload failed. This request would have overwritten an existing package.");
                }
                else if (result.StatusCode.Equals(HttpStatusCode.Unauthorized) || result.StatusCode.Equals(HttpStatusCode.Forbidden))
                {
                    throw new CakeException("Authorization to ProGet server failed; Credentials were incorrect, or not supplied.");
                }
            }
            else
            {
                // the following is generally adapted from inedo documentation: https://inedo.com/support/documentation/proget/reference/asset-directories-api
                using (var fs = new FileStream(asset.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan))
                {
                    var length = fs.Length;
                    long remainder;
                    var totalParts = Math.DivRem(length, ChunkSize, out remainder);
                    if (remainder != 0)
                    {
                        totalParts++;
                    }

                    // provide stateful byte copier so we can be less chatty about logging progress.
                    var copier = new ByteCopier(_log, length);

                    var uuid = Guid.NewGuid().ToString("N");
                    for (var index = 0; index < totalParts; index++)
                    {
                        var offset = index * ChunkSize;
                        var currentChunkSize = ChunkSize;
                        if (index == (totalParts - 1))
                        {
                            currentChunkSize = (int)length - offset;
                        }
                        var client = (HttpWebRequest)WebRequest
                            .Create($"{uri}?multipart=upload&id={uuid}&index={index}&offset={offset}&totalSize={length}&partSize={currentChunkSize}&totalParts={totalParts}");

                        client.Method = "POST";
                        client.ContentLength = currentChunkSize;
                        client.AllowWriteStreamBuffering = false;

                        _configuration.Apply(client);

                        using (var requestStream = client.GetRequestStream())
                        {
                            copier.CopyMaxBytes(fs, requestStream, currentChunkSize, offset);
                        }

                        try
                        {
                            using (client.GetResponse())
                            {
                            }
                        }
                        catch (WebException ex)
                        {
                            throw new CakeException($"Exception occurred while uploading part {index}. HTTP status was {((HttpWebResponse)ex.Response)?.StatusCode.ToString() ?? "unknown"}", ex);
                        }
                    }

                    _log.Information("Completing upload...");
                    var completeClient = (HttpWebRequest)WebRequest.Create($"{uri}?multipart=complete&id={uuid}");
                    completeClient.Method = "POST";
                    completeClient.ContentLength = 0;

                    _configuration.Apply(completeClient);

                    try
                    {
                        using (completeClient.GetResponse())
                        {
                        }
                    }
                    catch (WebException ex)
                    {
                        throw new CakeException($"Exception occurred while finalizing multipart upload. HTTP status was {((HttpWebResponse)ex.Response)?.StatusCode.ToString() ?? "unknown"}", ex);
                    }
                }
            }
        }

        private sealed class ByteCopier
        {
            private readonly long _total;
            private readonly ICakeLog _log;

            private int _lastProgress;

            public ByteCopier(ICakeLog log, long total)
            {
                _total = total;
                _log = log ?? throw new ArgumentNullException(nameof(log));
            }

            public void CopyMaxBytes(Stream source, Stream target, int maxBytes, long startOffset)
            {
                var buffer = new byte[32767];
                var totalRead = 0;
                while (true)
                {
                    var bytesRead = source.Read(buffer, 0, Math.Min(maxBytes - totalRead, buffer.Length));
                    if (bytesRead == 0)
                    {
                        break;
                    }

                    target.Write(buffer, 0, bytesRead);

                    totalRead += bytesRead;

                    if (totalRead >= maxBytes)
                    {
                        break;
                    }

                    // don't be so verbose!
                    var current = Math.Round((startOffset + totalRead) / (double)_total * 100, 2);
                    if ((int)current > _lastProgress)
                    {
                        _log.Debug($"Uploading ... {current}%");
                        _lastProgress = (int)current;
                    }
                }
            }
        }
    }
}