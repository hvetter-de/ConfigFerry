using System.Text;
using ConfigFerry.Core.Abstractions;

namespace ConfigFerry.Core.Azure;

public sealed class TextFileStore : ITextFileStore
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public bool Exists(string path) => File.Exists(path);

    public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken) =>
        File.ReadAllTextAsync(path, cancellationToken);

    public Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken) =>
        File.WriteAllTextAsync(path, content, Utf8NoBom, cancellationToken);

    public void Copy(string source, string destination, bool overwrite) => File.Copy(source, destination, overwrite);
}
