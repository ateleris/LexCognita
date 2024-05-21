

namespace SharedWebComponents.Services;

public interface IPdfViewer
{
    ValueTask ShowDocumentAsync(string name, string baseUrl);
}
