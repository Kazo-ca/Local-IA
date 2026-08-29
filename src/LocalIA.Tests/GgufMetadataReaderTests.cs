using LocalIA.Core.Gguf;

namespace LocalIA.Tests;

public class GgufMetadataReaderTests
{
    private const uint TypeUint32 = 4;
    private const uint TypeString = 8;
    private const int GgmlTypeF32 = 0;

    private sealed class GgufBuilder
    {
        private readonly List<(string Key, uint Type, object Value)> _metadata = [];
        private readonly List<(string Name, long[] Dims, int GgmlType)> _tensors = [];

        public GgufBuilder WithMetadata(string key, uint type, object value)
        {
            _metadata.Add((key, type, value));
            return this;
        }

        public GgufBuilder WithTensor(string name, long[] dims, int ggmlType)
        {
            _tensors.Add((name, dims, ggmlType));
            return this;
        }

        public string WriteToTempFile()
        {
            var path = Path.Combine(Path.GetTempPath(), $"gguf-test-{Guid.NewGuid():N}.gguf");
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
            using var writer = new BinaryWriter(stream);

            writer.Write(0x46554747u); // magic "GGUF"
            writer.Write(3u); // version
            writer.Write((ulong)_tensors.Count);
            writer.Write((ulong)_metadata.Count);

            foreach (var (key, type, value) in _metadata)
            {
                WriteString(writer, key);
                writer.Write(type);
                switch (type)
                {
                    case TypeUint32: writer.Write((uint)value); break;
                    case TypeString: WriteString(writer, (string)value); break;
                    default: throw new NotSupportedException($"Type de métadonnée non géré par ce builder de test : {type}");
                }
            }

            foreach (var (name, dims, ggmlType) in _tensors)
            {
                WriteString(writer, name);
                writer.Write((uint)dims.Length);
                foreach (var dim in dims)
                {
                    writer.Write((ulong)dim);
                }

                writer.Write((uint)ggmlType);
                writer.Write((ulong)0); // offset — non lu par GgufMetadataReader pour les métadonnées
            }

            return path;
        }

        private static void WriteString(BinaryWriter writer, string value)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(value);
            writer.Write((ulong)bytes.Length);
            writer.Write(bytes);
        }
    }

    [Fact]
    public void Read_MultiShardModel_ThrowsInvalidDataException()
    {
        var path = new GgufBuilder()
            .WithMetadata("split.count", TypeUint32, 3u)
            .WriteToTempFile();
        try
        {
            var reader = new GgufMetadataReader();

            var ex = Assert.Throws<InvalidDataException>(() => reader.Read(path));
            Assert.Contains("3", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Read_SingleFileModel_DoesNotThrow()
    {
        var path = new GgufBuilder()
            .WithMetadata("general.architecture", TypeString, "testarch")
            .WithMetadata("testarch.block_count", TypeUint32, 1u)
            .WriteToTempFile();
        try
        {
            var reader = new GgufMetadataReader();
            var metadata = reader.Read(path);

            Assert.Equal(1, metadata.BlockCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Read_ExpertTensorName_ClassifiedAsMoeNotDense()
    {
        // Régression : un vrai nom de tenseur d'expert ("blk.0.ffn_gate_exps.weight") contient à la
        // fois "_exps." et ".ffn_" — l'ordre du if/else if dans BuildLayers est donc significatif.
        // Ce test verrouille le comportement attendu contre un réordonnancement accidentel.
        var path = new GgufBuilder()
            .WithMetadata("general.architecture", TypeString, "testarch")
            .WithMetadata("testarch.block_count", TypeUint32, 1u)
            .WithTensor("blk.0.ffn_gate_exps.weight", [2, 2], GgmlTypeF32)
            .WithTensor("blk.0.ffn_gate.weight", [2, 2], GgmlTypeF32)
            .WithTensor("blk.0.attn_q.weight", [2, 2], GgmlTypeF32)
            .WriteToTempFile();
        try
        {
            var reader = new GgufMetadataReader();
            var metadata = reader.Read(path);

            var layer = Assert.Single(metadata.Layers);
            Assert.True(layer.IsMoeLayer);
            Assert.True(layer.MoeExpertsSizeBytes > 0);
            Assert.True(layer.DenseFfnSizeBytes > 0);
            Assert.True(layer.AttentionSizeBytes > 0);
            Assert.True(metadata.IsMoe);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
