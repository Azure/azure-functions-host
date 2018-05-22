// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Net;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Managment
{
    public class VirtualFileSystemFacts : IDisposable
    {
        private const string SiteRootPath = @"C:\DWASFiles\Sites\SiteName\VirtualDirectory0";
        private static readonly string LocalSiteRootPath = Path.GetFullPath(Path.Combine(SiteRootPath, @".."));

        public static IEnumerable<object[]> MapRouteToLocalPathData
        {
            get
            {
                yield return new object[] { "https://localhost/vfs", SiteRootPath };
                yield return new object[] { "https://localhost/vfs/LogFiles/kudu", SiteRootPath + @"\LogFiles\kudu" };

                yield return new object[] { "https://localhost/vfs/SystemDrive", "%SystemDrive%" };
                yield return new object[] { "https://localhost/vfs/SystemDrive/windows", @"%SystemDrive%\windows" };
                yield return new object[] { "https://localhost/vfs/SystemDrive/Program Files (x86)", @"%ProgramFiles(x86)%" };

                yield return new object[] { "https://localhost/vfs/LocalSiteRoot", LocalSiteRootPath };
                yield return new object[] { "https://localhost/vfs/LocalSiteRoot/Temp", LocalSiteRootPath + @"\Temp" };
            }
        }

        public static IEnumerable<object[]> DeleteItemDeletesFileIfETagMatchesData
        {
            get
            {
                yield return new object[] { EntityTagHeaderValue.Any };
                yield return new object[] { new EntityTagHeaderValue("\"00c0b16b2129cf08\"") };
            }
        }

        [Fact]
        public async Task DeleteRequestReturnsNotFoundIfItemDoesNotExist()
        {
            // Arrange
            string path = @"/foo/bar/";
            var fileInfo = new Mock<FileInfoBase>();
            fileInfo.SetupGet(f => f.Attributes).Returns((FileAttributes)(-1));
            var dirInfo = new Mock<DirectoryInfoBase>();
            dirInfo.SetupGet(d => d.Attributes).Returns((FileAttributes)(-1));
            var fileSystem = CreateFileSystem(path, dirInfo.Object, fileInfo.Object);
            var controller = CreateVirtualFileSystem();
            FileUtility.Instance = fileSystem;

            // Act
            var result = await controller.DeleteItem(CreateRequest(path));

            // Assert
            Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
        }

        [Fact]
        public async Task DeleteRequestDoesNotRecursivelyDeleteDirectoriesByDefault()
        {
            // Arrange
            string path = @"/foo/bar/";
            var fileInfo = new Mock<FileInfoBase>();
            fileInfo.SetupGet(f => f.Attributes).Returns(FileAttributes.Directory);
            var dirInfo = new Mock<DirectoryInfoBase>();
            dirInfo.SetupGet(d => d.Attributes).Returns(FileAttributes.Directory);
            var fileSystem = CreateFileSystem(path, dirInfo.Object, fileInfo.Object);
            FileUtility.Instance = fileSystem;

            var controller = CreateVirtualFileSystem();

            // Act
            await controller.DeleteItem(CreateRequest(path));

            // Assert
            dirInfo.Verify(d => d.Delete(false), Times.Once());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task DeleteRequestInvokesRecursiveDeleteBasedOnParameter(bool recursive)
        {
            // Arrange
            string path = @"/foo/bar/";
            var fileInfo = new Mock<FileInfoBase>();
            fileInfo.SetupGet(f => f.Attributes).Returns(FileAttributes.Directory);
            var dirInfo = new Mock<DirectoryInfoBase>();
            dirInfo.SetupGet(d => d.Attributes).Returns(FileAttributes.Directory);
            var fileSystem = CreateFileSystem(path, dirInfo.Object, fileInfo.Object);
            var controller = CreateVirtualFileSystem();
            FileUtility.Instance = fileSystem;

            // Act
            var response = await controller.DeleteItem(CreateRequest(path), recursive);

            // Assert
            dirInfo.Verify(d => d.Delete(recursive), Times.Once());
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task DeleteItemReturnsPreconditionFailedResponseIfFileDeleteDoesNotContainETag()
        {
            // Arrange
            string path = @"/foo/bar.txt";
            var fileInfo = new Mock<FileInfoBase>();
            fileInfo.SetupGet(f => f.Attributes).Returns(FileAttributes.Normal);
            var dirInfo = new Mock<DirectoryInfoBase>();
            dirInfo.SetupGet(d => d.Attributes).Returns(FileAttributes.Normal);
            var fileSystem = CreateFileSystem(path, dirInfo.Object, fileInfo.Object);
            FileUtility.Instance = fileSystem;

            var controller = CreateVirtualFileSystem();

            // Act
            var response = await controller.DeleteItem(CreateRequest(path));

            // Assert
            Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
        }

        [Fact]
        public async Task DeleteItemReturnsPreconditionFailedIfETagDoesNotMatch()
        {
            // Arrange
            var date = new DateTime(2012, 07, 06);
            string path = @"/foo/bar.txt";
            var fileInfo = new Mock<FileInfoBase>();
            fileInfo.SetupGet(f => f.Attributes).Returns(FileAttributes.Normal);
            fileInfo.SetupGet(f => f.LastWriteTimeUtc).Returns(date);
            var dirInfo = new Mock<DirectoryInfoBase>();
            dirInfo.SetupGet(d => d.Attributes).Returns(FileAttributes.Normal);
            var fileSystem = CreateFileSystem(path, dirInfo.Object, fileInfo.Object);
            FileUtility.Instance = fileSystem;

            var controller = CreateVirtualFileSystem();
            var request = CreateRequest(path);
            request.Headers.TryAdd("If-Match", "will-not-match");

            // Act
            var response = await controller.DeleteItem(request);

            // Assert
            Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
        }

        [Theory]
        [MemberData(nameof(DeleteItemDeletesFileIfETagMatchesData))]
        public async Task DeleteItemDeletesFileIfETagMatches(EntityTagHeaderValue etag)
        {
            // Arrange
            var date = new DateTime(2012, 07, 06);
            string path = @"/foo/bar.txt";
            var fileInfo = new Mock<FileInfoBase>();
            fileInfo.SetupGet(f => f.Attributes).Returns(FileAttributes.Normal);
            fileInfo.SetupGet(f => f.LastWriteTimeUtc).Returns(date);
            var dirInfo = new Mock<DirectoryInfoBase>();
            dirInfo.SetupGet(d => d.Attributes).Returns(FileAttributes.Normal);
            var fileSystem = CreateFileSystem(path, dirInfo.Object, fileInfo.Object);
            FileUtility.Instance = fileSystem;

            var controller = CreateVirtualFileSystem();
            var request = CreateRequest(path);
            request.Headers.Add("If-Match", etag.Tag);

            // Act
            var response = await controller.DeleteItem(request);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            fileInfo.Verify(f => f.Delete());
        }

        private static HttpRequest CreateRequest(string path)
        {
            var r = new DefaultHttpContext().Request;
            r.Host = new HostString("localhost");
            r.Path = new PathString("/admin/vfs" + path);
            return r;
        }

        private static IFileSystem CreateFileSystem(string path, DirectoryInfoBase dir, FileInfoBase file)
        {
            var directoryFactory = new Mock<IDirectoryInfoFactory>();
            directoryFactory.Setup(d => d.FromDirectoryName(It.IsAny<string>()))
                            .Returns(dir);
            var fileInfoFactory = new Mock<IFileInfoFactory>();
            fileInfoFactory.Setup(f => f.FromFileName(It.IsAny<string>()))
                           .Returns(file);

            var pathBase = new Mock<PathBase>();
            pathBase.Setup(p => p.GetFullPath(It.IsAny<string>()))
                    .Returns<string>(s => s);

            var fileSystem = new Mock<IFileSystem>();
            fileSystem.SetupGet(f => f.DirectoryInfo).Returns(directoryFactory.Object);
            fileSystem.SetupGet(f => f.FileInfo).Returns(fileInfoFactory.Object);
            fileSystem.SetupGet(f => f.Path).Returns(pathBase.Object);

            FileUtility.Instance = fileSystem.Object;

            return fileSystem.Object;
        }

        private VirtualFileSystem CreateVirtualFileSystem()
        {
            return new VirtualFileSystem(new WebHostSettings
            {
                ScriptPath = SiteRootPath
            }, NullLoggerFactory.Instance);
        }

        public void Dispose()
        {
            // clear FileUtility.Instance after this test is done
            FileUtility.Instance = null;
        }
    }
}
