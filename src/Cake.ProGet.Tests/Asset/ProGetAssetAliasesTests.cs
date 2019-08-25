﻿using Cake.Core.IO;
using Cake.ProGet.Asset;
using Xunit;

namespace Cake.ProGet.Tests.Asset
{
    public sealed class AssetAliasesTests
    {
        public sealed class ThePusherAliases
        {
            [Fact]
            public void Should_Throw_If_Cake_Context_Is_Null()
            {
                var result = Record.Exception(() =>
                    ProGetAssetAliases.ProGetPushAsset(null, new FilePath("foo"), "http://foo.com", new ProGetConfiguration()));
                ExtraAssert.IsArgumentNullException(result, "context");
            }
        }

        public sealed class TheListerAliases
        {
            [Fact]
            public void Should_Throw_If_Cake_Context_Is_Null()
            {
                var result = Record.Exception(() =>
                        ProGetAssetAliases.ProGetCreateAssetDirectory(null, "http://localhost:9091/content/testdir",
                    new ProGetConfiguration()));
                ExtraAssert.IsArgumentNullException(result, "context");
            }
        }

        public sealed class TheDownloaderAliases
        {
            [Fact]
            public void Should_Throw_If_Cake_Context_Is_Null()
            {
                var result = Record.Exception(() => ProGetAssetAliases.ProGetDownloadAsset(null,
                    "http://localhost:9091/content/testdir/asset.gif", "./out/asset.gif", new ProGetConfiguration()));
                ExtraAssert.IsArgumentNullException(result, "context");
            }
        }
    }
}
