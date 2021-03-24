// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Azure.WebJobs.Script.Workers.SharedMemoryDataTransfer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers
{
    /// <summary>
    /// Tests for <see cref="MemoryMappedFileAccessor"/>.
    /// Based on which platform these are run on, one of <see cref="MemoryMappedFileAccessorUnix"/> or <see cref="MemoryMappedFileAccessorWindows"/> will be created.
    /// </summary>
    public class MemoryMappedFileAccessorTests
    {
        private readonly IEnvironment _testEnvironment;
        private readonly IMemoryMappedFileAccessor _mapAccessor;

        public MemoryMappedFileAccessorTests()
        {
            ILogger<MemoryMappedFileAccessor> logger = NullLogger<MemoryMappedFileAccessor>.Instance;
            _testEnvironment = new TestEnvironment();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _mapAccessor = new MemoryMappedFileAccessorWindows(logger);
            }
            else
            {
                _mapAccessor = new MemoryMappedFileAccessorUnix(logger, _testEnvironment);
            }
        }

        /// <summary>
        /// Verify that when the AppSetting is specified, the directory used for shared memory is picked from the AppSetting and the default is not used.
        /// </summary>
        [Fact]
        public void Use_AppSetting_Directory_Unix()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return;
            }

            IEnvironment testEnv = new TestEnvironment();
            ILogger<MemoryMappedFileAccessor> logger = NullLogger<MemoryMappedFileAccessor>.Instance;

            string directory = "/tmp/shm";
            testEnv.SetEnvironmentVariable(RpcWorkerConstants.FunctionsUnixSharedMemoryDirectories, directory);

            MemoryMappedFileAccessorUnix mapAccessor = new MemoryMappedFileAccessorUnix(logger, testEnv);

            string expectedDirectory = $"/tmp/shm/{SharedMemoryConstants.TempDirSuffix}";
            Assert.Single(mapAccessor.ValidDirectories);
            Assert.Contains(expectedDirectory, mapAccessor.ValidDirectories);
        }

        /// <summary>
        /// Test the allowed directories for shared memory without setting the AppSetting; this should use the default.
        /// </summary>
        [Fact]
        public void Verify_AllowedDirectory_Default_Unix()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return;
            }

            IEnvironment testEnv = new TestEnvironment();
            ILogger<MemoryMappedFileAccessor> logger = NullLogger<MemoryMappedFileAccessor>.Instance;
            MemoryMappedFileAccessorUnix mapAccessor = new MemoryMappedFileAccessorUnix(logger, testEnv);

            List<string> allowedDirectories = mapAccessor.GetAllowedDirectories();
            Assert.Equal(SharedMemoryConstants.TempDirs, allowedDirectories);
        }

        /// <summary>
        /// Test the allowed directories for shared memory with a single allowed directory specified in the AppSetting; this should override the default.
        /// </summary>
        [Fact]
        public void Verify_Single_AllowedDirectory_Via_AppSetting_Unix()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return;
            }

            IEnvironment testEnv = new TestEnvironment();
            ILogger<MemoryMappedFileAccessor> logger = NullLogger<MemoryMappedFileAccessor>.Instance;
            MemoryMappedFileAccessorUnix mapAccessor = new MemoryMappedFileAccessorUnix(logger, testEnv);

            string directory = "/tmp/shm";
            testEnv.SetEnvironmentVariable(RpcWorkerConstants.FunctionsUnixSharedMemoryDirectories, directory);

            List<string> allowedDirectories = mapAccessor.GetAllowedDirectories();
            Assert.Contains(directory, allowedDirectories);
            Assert.Equal(1, allowedDirectories.Count);
        }

        /// <summary>
        /// Test the allowed directories for shared memory with a list of allowed directories specified in the AppSetting; this should override the default.
        /// </summary>
        [Fact]
        public void Verify_Multiple_AllowedDirectories_Via_AppSetting_Unix()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return;
            }

            IEnvironment testEnv = new TestEnvironment();
            ILogger<MemoryMappedFileAccessor> logger = NullLogger<MemoryMappedFileAccessor>.Instance;
            MemoryMappedFileAccessorUnix mapAccessor = new MemoryMappedFileAccessorUnix(logger, testEnv);

            List<string> directories = new List<string>()
            {
                "/tmp/shm1",
                "/tmp/shm2",
                "/tmp/shm3",
            };
            testEnv.SetEnvironmentVariable(RpcWorkerConstants.FunctionsUnixSharedMemoryDirectories, string.Join(",", directories));

            List<string> allowedDirectories = mapAccessor.GetAllowedDirectories();
            Assert.All(allowedDirectories, (x) => allowedDirectories.Contains(x));
            Assert.Equal(directories.Count, allowedDirectories.Count);

            // Test with a list that does not have the right delimiter; it will be returned as a single directory
            string directory = "/tmp/shm-/tmp/shm";
            testEnv.SetEnvironmentVariable(RpcWorkerConstants.FunctionsUnixSharedMemoryDirectories, directory);

            allowedDirectories = mapAccessor.GetAllowedDirectories();
            Assert.Contains(directory, allowedDirectories);
            Assert.Equal(1, allowedDirectories.Count);
        }

        /// <summary>
        /// Verify if the list of valid directories being used matches those specified in AppSetting.
        /// The directory is not already there, it should be created.
        /// </summary>
        [Fact]
        public void Verify_Single_NonExistent_ValidDirectory_Via_AppSetting_Unix()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return;
            }

            IEnvironment testEnv = new TestEnvironment();
            ILogger<MemoryMappedFileAccessor> logger = NullLogger<MemoryMappedFileAccessor>.Instance;
            MemoryMappedFileAccessorUnix mapAccessor = new MemoryMappedFileAccessorUnix(logger, testEnv);

            string directory = "/tmp/shm";
            testEnv.SetEnvironmentVariable(RpcWorkerConstants.FunctionsUnixSharedMemoryDirectories, directory);

            List<string> validDirectories = mapAccessor.GetValidDirectories();
            string expectedDirectory = $"{directory}/{SharedMemoryConstants.TempDirSuffix}";
            Assert.Contains(expectedDirectory, validDirectories);
            Assert.Equal(1, validDirectories.Count);

            // Cleanup
            Directory.Delete(expectedDirectory, recursive: true);
        }

        /// <summary>
        /// Verify if the list of valid directories being used matches those specified in AppSetting.
        /// The directory is already there, it should be cleaned up and a new one created.
        /// </summary>
        [Fact]
        public void Verify_Single_AlreadyExistent_ValidDirectory_Via_AppSetting_Unix()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return;
            }

            IEnvironment testEnv = new TestEnvironment();
            ILogger<MemoryMappedFileAccessor> logger = NullLogger<MemoryMappedFileAccessor>.Instance;
            MemoryMappedFileAccessorUnix mapAccessor = new MemoryMappedFileAccessorUnix(logger, testEnv);

            string directory = "/tmp/shm1";
            testEnv.SetEnvironmentVariable(RpcWorkerConstants.FunctionsUnixSharedMemoryDirectories, directory);

            // The directory should already be there before MemoryMappedFileAccessor checks for it
            string directoryWithSuffix = $"{directory}/{SharedMemoryConstants.TempDirSuffix}";
            DirectoryInfo dirInfo = Directory.CreateDirectory(directoryWithSuffix);
            DateTime oldCreationTime = dirInfo.CreationTimeUtc;

            // Sleep for 10ms so that the timestamp on when we created the directory above and when MemoryMappedFileAccessor creates the directory has significant difference.
            // This will make it possible to check if the directory was created again by MemoryMappedFileAccessor by comparing the timestamps.
            Thread.Sleep(10);

            List<string> validDirectories = mapAccessor.GetValidDirectories();
            Assert.Contains(directoryWithSuffix, validDirectories);
            Assert.Equal(1, validDirectories.Count);

            // Now check if MemoryMappedFileAccessor had cleaned and created the directory again
            DateTime newCreationTime = new DirectoryInfo(directory).CreationTimeUtc;
            Assert.True(newCreationTime > oldCreationTime);

            // Cleanup
            Directory.Delete(directoryWithSuffix, recursive: true);
        }

        /// <summary>
        /// Create a <see cref="MemoryMappedFile"/> with valid input.
        /// Verify that it is created.
        /// </summary>
        /// <param name="size">Size of <see cref="MemoryMappedFile"/>.</param>
        [InlineData(10)]
        [InlineData(100)]
        [InlineData(1024)]
        [Theory]
        public void Create_VerifyCreated(long size)
        {
            string mapName = Guid.NewGuid().ToString();

            Assert.True(_mapAccessor.TryCreate(mapName, size, out MemoryMappedFile mmf));
            Assert.NotNull(mmf);

            _mapAccessor.Delete(mapName, mmf);
        }

        /// <summary>
        /// Create a <see cref="MemoryMappedFile"/> with invalid size.
        /// Verify that it is not created.
        /// </summary>
        /// <param name="size">Size of <see cref="MemoryMappedFile"/>.</param>
        [InlineData(0)]
        [InlineData(-1)]
        [Theory]
        public void Create_InvalidSize_VerifyNotCreated(long size)
        {
            string mapName = Guid.NewGuid().ToString();

            Assert.False(_mapAccessor.TryCreate(mapName, size, out MemoryMappedFile mmf));
            Assert.Null(mmf);
        }

        /// <summary>
        /// Create a <see cref="MemoryMappedFile"/> with invalid name.
        /// Verify that it is not created.
        /// </summary>
        /// <param name="mapName">Name of <see cref="MemoryMappedFile"/>.</param>
        [InlineData(null)]
        [InlineData("")]
        [Theory]
        public void Create_InvalidMapName_VerifyNotCreated(string mapName)
        {
            long size = 1024;

            Assert.False(_mapAccessor.TryCreate(mapName, size, out MemoryMappedFile mmf));
            Assert.Null(mmf);
        }

        /// <summary>
        /// Create a <see cref="MemoryMappedFile"/> with the same name as one that already exists.
        /// Verify that it is not created again.
        /// </summary>
        [Fact]
        public void Create_AlreadyExists_VerifyNotCreated()
        {
            long size = 1024;
            string mapName = Guid.NewGuid().ToString();

            // Create once
            Assert.True(_mapAccessor.TryCreate(mapName, size, out MemoryMappedFile mmf1));
            Assert.NotNull(mmf1);

            // Attempt to create again with same name
            Assert.False(_mapAccessor.TryCreate(mapName, size, out MemoryMappedFile mmf2));
            Assert.Null(mmf2);
        }

        /// <summary>
        /// Create a <see cref="MemoryMappedFile"/> and try to open it.
        /// Verify that it is opened.
        /// </summary>
        [Fact]
        public void Create_Open_VerifyOpened()
        {
            long size = 1024;
            string mapName = Guid.NewGuid().ToString();

            // Create
            Assert.True(_mapAccessor.TryCreate(mapName, size, out MemoryMappedFile mmf1));
            Assert.NotNull(mmf1);

            // Open
            Assert.True(_mapAccessor.TryOpen(mapName, out MemoryMappedFile mmf2));
            Assert.NotNull(mmf2);

            _mapAccessor.Delete(mapName, mmf1);
            _mapAccessor.Delete(mapName, mmf2);
        }

        /// <summary>
        /// Open a <see cref="MemoryMappedFile"/> that does not exist.
        /// Verify that is is not opened.
        /// </summary>
        [Fact]
        public void Open_NonExistent_VerifyNotOpened()
        {
            string mapName = Guid.NewGuid().ToString();

            // Attempt to open a map that was not created
            Assert.False(_mapAccessor.TryOpen(mapName, out MemoryMappedFile mmf));
            Assert.Null(mmf);
        }

        /// <summary>
        /// CreateOrOpen a <see cref="MemoryMappedFile"/> that does not exist.
        /// Verify that is is created.
        /// </summary>
        [Fact]
        public void CreateOrOpen_NonExistent_VerifyCreated()
        {
            long size = 1024;
            string mapName = Guid.NewGuid().ToString();

            // Create a new map
            Assert.True(_mapAccessor.TryCreateOrOpen(mapName, size, out MemoryMappedFile mmf));
            Assert.NotNull(mmf);

            _mapAccessor.Delete(mapName, mmf);
        }

        /// <summary>
        /// Create a <see cref="MemoryMappedFile"/> and CreateOrOpen it.
        /// Verify that it is opened.
        /// </summary>
        [Fact]
        public void CreateOrOpen_Existent_VerifyOpened()
        {
            long size = 1024;
            string mapName = Guid.NewGuid().ToString();

            // Create a new map
            Assert.True(_mapAccessor.TryCreate(mapName, size, out MemoryMappedFile mmf1));
            Assert.NotNull(mmf1);

            // Open created map
            Assert.True(_mapAccessor.TryCreateOrOpen(mapName, size, out MemoryMappedFile mmf2));
            Assert.NotNull(mmf2);

            _mapAccessor.Delete(mapName, mmf1);
            _mapAccessor.Delete(mapName, mmf2);
        }

        /// <summary>
        /// CreateOrOpen a <see cref="MemoryMappedFile"/> which will create it, then again CreateOrOpen it.
        /// Verify that it is opened.
        /// </summary>
        [Fact]
        public void CreateOrOpen_VerifyCreated_VerifyOpened()
        {
            long size = 1024;
            string mapName = Guid.NewGuid().ToString();

            // Create a new map
            Assert.True(_mapAccessor.TryCreateOrOpen(mapName, size, out MemoryMappedFile mmf1));
            Assert.NotNull(mmf1);

            // Open created map
            Assert.True(_mapAccessor.TryCreateOrOpen(mapName, size, out MemoryMappedFile mmf2));
            Assert.NotNull(mmf2);

            _mapAccessor.Delete(mapName, mmf1);
            _mapAccessor.Delete(mapName, mmf2);
        }

        /// <summary>
        /// CreateOrOpen a <see cref="MemoryMappedFile"/> which will create it, then Open it.
        /// Verify that it is opened.
        /// </summary>
        [Fact]
        public void CreateOrOpen_Open_VerifyOpened()
        {
            long size = 1024;
            string mapName = Guid.NewGuid().ToString();

            // Create a new map
            Assert.True(_mapAccessor.TryCreateOrOpen(mapName, size, out MemoryMappedFile mmf1));
            Assert.NotNull(mmf1);

            // Open created map
            Assert.True(_mapAccessor.TryOpen(mapName, out MemoryMappedFile mmf2));
            Assert.NotNull(mmf2);

            _mapAccessor.Delete(mapName, mmf1);
            _mapAccessor.Delete(mapName, mmf2);
        }

        /// <summary>
        /// Create a <see cref="MemoryMappedFile"/>, then delete it.
        /// Verify that it cannot be opened after deleting it.
        /// </summary>
        [Fact]
        public void Create_Delete_VerifyDeleted()
        {
            long size = 1024;
            string mapName = Guid.NewGuid().ToString();

            // Create a new map
            Assert.True(_mapAccessor.TryCreate(mapName, size, out MemoryMappedFile mmf1));
            Assert.NotNull(mmf1);

            // Ensure that the map can be opened
            Assert.True(_mapAccessor.TryOpen(mapName, out MemoryMappedFile mmf2));
            Assert.NotNull(mmf2);
            mmf2.Dispose();

            // Delete it
            _mapAccessor.Delete(mapName, mmf1);

            // Attempt to open it; since it is deleted, it should not be opened
            Assert.False(_mapAccessor.TryOpen(mapName, out MemoryMappedFile mmf3));
            Assert.Null(mmf3);
        }

        /// <summary>
        /// CreateOrOpen a <see cref="MemoryMappedFile"/> which will create it, then delete it.
        /// Verify that it cannot be opened after deleting it.
        /// </summary>
        [Fact]
        public void CreateOrOpen_Delete_VerifyDeleted()
        {
            long size = 1024;
            string mapName = Guid.NewGuid().ToString();

            // Create a new map
            Assert.True(_mapAccessor.TryCreateOrOpen(mapName, size, out MemoryMappedFile mmf1));
            Assert.NotNull(mmf1);

            // Delete it
            _mapAccessor.Delete(mapName, mmf1);

            // Attempt to open it; since it is deleted, it should not be opened
            Assert.False(_mapAccessor.TryOpen(mapName, out MemoryMappedFile mmf2));
            Assert.Null(mmf2);
        }

        /// <summary>
        /// Create a <see cref="MemoryMappedFile"/>, open two more references to it.
        /// Delete first and second references, on Windows it should still be opened (as all references are not dropped).
        /// Then delete third and final reference.
        /// On Windows, verify that it cannot be opened after deleting all references.
        /// On Linux, deleting the first reference causes the backing file to be deleted and hence the map is deleted.
        /// Note:
        /// Behaviour on Linux vs Windows is a little different.
        /// On Linux, the first Delete will remove the map no matter how many refs are open.
        /// On Windows, when delete is called once, it removes one ref.
        /// Since there are no files backing the map on Windows, it stays alive until all refs are open.
        /// </summary>
        [Fact]
        public void Create_OpenMultipleReferences_Delete_VerifyDeleted()
        {
            long size = 1024;
            string mapName = Guid.NewGuid().ToString();

            // Create a new map
            Assert.True(_mapAccessor.TryCreate(mapName, size, out MemoryMappedFile mmf1));
            Assert.NotNull(mmf1);

            // Open a reference to the map
            Assert.True(_mapAccessor.TryOpen(mapName, out MemoryMappedFile mmf2));
            Assert.NotNull(mmf2);

            // Open another reference to the map
            Assert.True(_mapAccessor.TryOpen(mapName, out MemoryMappedFile mmf3));
            Assert.NotNull(mmf3);

            // Delete first reference
            _mapAccessor.Delete(mapName, mmf1);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Attempt to open it
                Assert.True(_mapAccessor.TryOpen(mapName, out MemoryMappedFile mmf4));
                Assert.NotNull(mmf4);
                _mapAccessor.Delete(mapName, mmf4);

                // Delete second reference
                _mapAccessor.Delete(mapName, mmf2);

                // Attempt to open it
                Assert.True(_mapAccessor.TryOpen(mapName, out MemoryMappedFile mmf5));
                Assert.NotNull(mmf5);
                _mapAccessor.Delete(mapName, mmf5);

                // Delete third (and final) reference
                _mapAccessor.Delete(mapName, mmf3);

                // Attempt to open it
                Assert.False(_mapAccessor.TryOpen(mapName, out MemoryMappedFile mmf6));
                Assert.Null(mmf6);
            }
            else
            {
                // Attempt to open it
                Assert.False(_mapAccessor.TryOpen(mapName, out MemoryMappedFile mmf4));
                Assert.Null(mmf4);
            }
        }
    }
}
