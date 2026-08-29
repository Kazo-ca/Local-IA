using LocalIA.Core.Gguf;

namespace LocalIA.Tests;

public class GgmlTypeTraitsTests
{
    [Fact]
    public void ComputeSizeBytes_UnknownType_ReturnsZero_NotAnException()
    {
        // Verrouille le repli documenté : un type ggml futur/non listé ne doit jamais planter la
        // lecture d'un GGUF, mais ce silence a un coût (sous-estimation de la VRAM nécessaire) —
        // ce test existe pour qu'un changement de ce comportement soit délibéré, pas accidentel.
        Assert.Equal(0, GgmlTypeTraits.ComputeSizeBytes(rawTypeId: 999, elementCount: 1000));
    }

    [Fact]
    public void ComputeSizeBytes_KnownType_ReturnsNonZero()
    {
        Assert.True(GgmlTypeTraits.ComputeSizeBytes(rawTypeId: (int)GgmlType.F32, elementCount: 1000) > 0);
    }
}
