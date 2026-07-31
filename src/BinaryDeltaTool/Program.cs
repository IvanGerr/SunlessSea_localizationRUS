using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

const int BlockSize = 64 * 1024;
var magic = Encoding.ASCII.GetBytes("SSRUDEL1");

if (args.Length != 4 || args[0] != "create")
{
    Console.Error.WriteLine("Usage: binary_delta_tool create <base-file> <target-file> <output-delta>");
    return 2;
}

var basePath = Path.GetFullPath(args[1]);
var targetPath = Path.GetFullPath(args[2]);
var outputPath = Path.GetFullPath(args[3]);
Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");

var baseInfo = new FileInfo(basePath);
var targetInfo = new FileInfo(targetPath);
var baseHash = HashFile(basePath);
var targetHash = HashFile(targetPath);

using var baseStream = File.OpenRead(basePath);
using var targetStream = File.OpenRead(targetPath);
using var outputStream = File.Create(outputPath);
using var gzip = new GZipStream(outputStream, CompressionLevel.SmallestSize, leaveOpen: false);
using var writer = new BinaryWriter(gzip, Encoding.UTF8, leaveOpen: false);

writer.Write(magic);
writer.Write(BlockSize);
writer.Write(baseInfo.Length);
writer.Write(targetInfo.Length);
writer.Write(baseHash);
writer.Write(targetHash);

var baseBuffer = new byte[BlockSize];
var targetBuffer = new byte[BlockSize];
long offset = 0;
long changedBytes = 0;
var changedBlocks = 0;

while (offset < targetInfo.Length)
{
    var targetCount = ReadBlock(targetStream, targetBuffer);
    var baseCount = ReadBlock(baseStream, baseBuffer);
    var equal = targetCount == baseCount &&
        targetBuffer.AsSpan(0, targetCount).SequenceEqual(baseBuffer.AsSpan(0, baseCount));

    if (!equal)
    {
        writer.Write(offset);
        writer.Write(targetCount);
        writer.Write(targetBuffer, 0, targetCount);
        changedBlocks++;
        changedBytes += targetCount;
    }

    offset += targetCount;
}

writer.Write(-1L);
writer.Flush();

Console.WriteLine($"Created {outputPath}");
Console.WriteLine($"Base:   {baseInfo.Length} bytes, {Convert.ToHexString(baseHash)}");
Console.WriteLine($"Target: {targetInfo.Length} bytes, {Convert.ToHexString(targetHash)}");
Console.WriteLine($"Changed blocks: {changedBlocks}; payload bytes before gzip: {changedBytes}");
return 0;

static int ReadBlock(Stream stream, byte[] buffer)
{
    var total = 0;
    while (total < buffer.Length)
    {
        var read = stream.Read(buffer, total, buffer.Length - total);
        if (read == 0)
            break;
        total += read;
    }
    return total;
}

static byte[] HashFile(string path)
{
    using var stream = File.OpenRead(path);
    return SHA256.HashData(stream);
}
