using System;
using System.IO;
using System.Net;
using Cake.Core.IO;
using Cake.ProGet.Asset;
using Cake.Testing;
using HttpMock;
using Xunit;
using Path = System.IO.Path;

namespace Cake.ProGet.Tests.Asset
{
    public sealed class ProGetAssetPusherTests : IDisposable
    {
        private const string Host = "http://localhost:9191";
        private readonly ProGetConfiguration _config;
        private readonly FakeLog _log;
        private readonly IHttpServer _server;
        
        public ProGetAssetPusherTests()
        {
            _config = new ProGetConfiguration
            {
                ProGetUser = "testuser",
                ProGetPassword = "password"
            };
            
            _log = new FakeLog();
            _server = HttpMockRepository.At(Host).WithNewContext();
        }
        
        [Theory]
        [InlineData("/endpoints/test/content/test.gif")]
        public void Should_Report_True_If_Asset_Exists(string assetUri)
        {
            _server.Stub(x => x.Head(assetUri))
                .OK();
            
            var asset = new ProGetAssetPusher(_log, _config );
            var result = asset.DoesAssetExist($"{Host}{assetUri}");
            Assert.True(result);
        }

        [Theory]
        [InlineData("/endpoints/test/content/test.gif")]
        public void Should_Report_False_If_Asset_Does_Not_Exist(string assetUri)
        {
            _server.Stub(x => x.Head(assetUri))
                .NotFound();
            
            var asset = new ProGetAssetPusher(_log, _config );
            var result = asset.DoesAssetExist($"{Host}{assetUri}");
            Assert.False(result);
        }

        [Theory]
        [InlineData("/endpoints/test/content/test.gif")]
        public void Should_Fail_Existence_Check_If_Creds_Are_Invalid(string assetUri)
        {
            _server.Stub(x => x.Head(assetUri))
                .WithStatus(HttpStatusCode.Unauthorized);
            
            var asset = new ProGetAssetPusher(_log, _config);
            var result = Record.Exception(() => asset.DoesAssetExist($"{Host}{assetUri}"));
            Assert.IsCakeException(result, "Authorization to ProGet server failed; Credentials were incorrect, or not supplied.");
        }

        [Theory]
        [InlineData("/endpoints/test/content/test.gif")]
        public void Should_Delete_Asset(string assetUri)
        {
            _server.Stub(x => x.Delete(assetUri))
                .OK();
            
            var asset = new ProGetAssetPusher(_log, _config);
            var result = asset.DeleteAsset($"{Host}{assetUri}");
            Assert.True(result);
        }

        [Theory]
        [InlineData("/endpoints/test/content/test.gif")]
        public void Should_Fail_Delete_If_Creds_Are_Invalid(string assetUri)
        {
            _server.Stub(x => x.Delete(assetUri))
                .WithStatus(HttpStatusCode.Unauthorized);
            
            var asset = new ProGetAssetPusher(_log, _config);
            var result = Record.Exception(() => asset.DeleteAsset($"{Host}{assetUri}"));
            Assert.IsCakeException(result, "Authorization to ProGet server failed; Credentials were incorrect, or not supplied.");
        }

        [Theory]
        [InlineData("/endpoints/test/content/test.gif")]
        public void Should_Push_New_Asset_With_Put_Under_5MB(string assetUri)
        {
            _server.Stub(x => x.Put(assetUri))
                .OK();
            
            var asset = new ProGetAssetPusher(_log, _config);
            var tempFile = new FilePath($"{Path.GetTempPath()}{Path.GetRandomFileName()}");
            using (var fileStream = new FileStream(tempFile.FullPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                fileStream.SetLength(4194304);
            }
            var result = Record.Exception(() => asset.Publish(tempFile, $"{Host}{assetUri}"));
            Assert.Null(result);

            try
            {
                File.Delete(tempFile.FullPath);
            }
            catch { /*suppress */ }
        }

        [Theory]
        [InlineData("/endpoints/test/content/test.gif")]
        public void Should_Push_New_Asset_With_Multipart_Post_Over_5MB(string assetUri)
        {
            _server.Stub(x => x.Post(assetUri))
                .OK();
            
            var asset = new ProGetAssetPusher(_log, _config);
            var tempFile = new FilePath($"{Path.GetTempPath()}{Path.GetRandomFileName()}");
            using (var fileStream = new FileStream(tempFile.FullPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                fileStream.SetLength(6291456);
            }
            var result = Record.Exception(() => asset.Publish(tempFile, $"{Host}{assetUri}"));
            Assert.Null(result);

            try
            {
                File.Delete(tempFile.FullPath);
            }
            catch { /*suppress */ }
        }
        
        [Theory]
        [InlineData("/endpoints/test/content/test.gif")]
        public void Should_Throw_Exception_When_Asset_Push_Fails_As_Put(string assetUri)
        {
            _server.Stub(x => x.Put(assetUri))
                .WithStatus(HttpStatusCode.BadRequest);
            
            var asset = new ProGetAssetPusher(_log, _config);
            var tempFile = new FilePath($"{Path.GetTempPath()}{Path.GetRandomFileName()}");

            using (var fileStream = new FileStream(tempFile.FullPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                fileStream.SetLength(4194304);
            }

            var result = Record.Exception(() => asset.Publish(tempFile, $"{Host}{assetUri}"));
            Assert.IsCakeException(result, "Upload failed. This request would have overwritten an existing package.");

            try
            {
                File.Delete(tempFile.FullPath);
            }
            catch { /*suppress */ }
        }

        public void Dispose()
        {
            _server.Dispose();
        }
    }
}