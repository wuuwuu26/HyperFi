using HyperFi.Streaming;
using Xunit;

namespace HyperFi.Core.Tests;

/// <summary>Verifies atomic publication of downloaded HyperFi Server backups.</summary>
public sealed class HyperFiServerBackupDownloadTests
{
    /// <summary>Ensures the temporary output stream is closed before the file is renamed on Windows.</summary>
    [Fact]
    public async Task WriteDownloadAtomicallyAsync_ClosesTemporaryFileBeforeRename()
    {
        var root = Path.Combine(Path.GetTempPath(), $"hyperfi-backup-download-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var destinationPath = Path.Combine(root, "backup.zip");
        var expected = new byte[] { 1, 2, 3, 4, 5 };

        try
        {
            await using var input = new MemoryStream(expected);
            await HyperFiServerClient.WriteDownloadAtomicallyAsync(
                input,
                destinationPath,
                expected.Length,
                progress: null,
                CancellationToken.None);

            Assert.Equal(expected, await File.ReadAllBytesAsync(destinationPath));
            Assert.False(File.Exists(destinationPath + ".tmp"));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
