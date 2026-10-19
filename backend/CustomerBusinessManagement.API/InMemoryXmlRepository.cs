using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;

sealed class InMemoryXmlRepository : IXmlRepository
{
    private readonly object sync = new();
    private readonly List<XElement> elements = new();

    public IReadOnlyCollection<XElement> GetAllElements()
    {
        lock (sync)
        {
            return elements.Select(element => new XElement(element)).ToArray();
        }
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        lock (sync)
        {
            elements.Add(new XElement(element));
        }
    }
}
