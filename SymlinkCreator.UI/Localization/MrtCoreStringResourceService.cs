using Microsoft.Windows.ApplicationModel.Resources;
using SymlinkCreator.Application.Presentation;

namespace SymlinkCreator.Localization;

public sealed class MrtCoreStringResourceService : IStringResourceService
{
    private readonly ResourceManager _resourceManager;
    private readonly ResourceMap _resourceMap;
    private readonly ResourceContext _resourceContext;

    public MrtCoreStringResourceService(string priPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(priPath);

        if (!File.Exists(priPath))
        {
            throw new FileNotFoundException("The external localization resource index was not found.", priPath);
        }

        _resourceManager = new ResourceManager(priPath);
        _resourceContext = _resourceManager.CreateResourceContext();
        _resourceMap = _resourceManager.MainResourceMap;
    }

    public void SetLanguage(string? language)
    {
        if (!string.IsNullOrWhiteSpace(language))
        {
            _resourceContext.QualifierValues["Language"] = language;
        }
    }

    public string GetString(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        string resourcePath = $"Resources/{key.Replace(".", "/", StringComparison.Ordinal)}";
        string? value = _resourceMap.GetValue(resourcePath, _resourceContext).ValueAsString;
        return string.IsNullOrEmpty(value) ? throw new KeyNotFoundException($"The localized resource key '{key}' was not found.") : value;
    }
}
