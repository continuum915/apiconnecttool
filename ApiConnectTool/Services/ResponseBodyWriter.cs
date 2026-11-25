using System;
using System.IO;
using System.Threading.Tasks;

namespace ApiConnectTool.Services;

public static class ResponseBodyWriter
{
    public static async Task<bool> SaveAsync(ReadOnlyMemory<byte> body, bool wasTruncated, Stream destination)
    {
        if (body.IsEmpty || wasTruncated) return false;
        if (destination.CanSeek) { destination.Position = 0; destination.SetLength(0); }
        await destination.WriteAsync(body);
        return true;
    }
}
