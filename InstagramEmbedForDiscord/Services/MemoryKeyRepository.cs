using Microsoft.AspNetCore.DataProtection.Repositories;
using System.Xml.Linq;

namespace InstagramEmbed.Application.Services;

// MVC initializes a key ring even with an ephemeral data-protection provider.
// Keep that initialization in memory so the container can stay read-only.
public sealed class MemoryKeyRepository : IXmlRepository
{
    private readonly List<XElement> _elements = [];
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        lock (_elements) return _elements.Select(e => new XElement(e)).ToArray();
    }
    public void StoreElement(XElement element, string friendlyName)
    {
        lock (_elements) _elements.Add(new XElement(element));
    }
}
