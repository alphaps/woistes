using Woistes.Domain;

namespace Woistes.CtfParser;

public interface ICtfParser
{
    Catalogue Parse(Stream stream, string sourceFileName);
}
