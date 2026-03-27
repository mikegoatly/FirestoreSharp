using Google.Protobuf;

namespace FirestoreSharp.Core
{
    public static class ByteStringExtensions
    {
#pragma warning disable CA1034 // Nested types should not be visible - false positive
        extension(ByteString)
#pragma warning restore CA1034 // Nested types should not be visible
        {
            public static ByteString New() => ByteString.CopyFrom(Guid.NewGuid().ToByteArray());
        }
    }
}
