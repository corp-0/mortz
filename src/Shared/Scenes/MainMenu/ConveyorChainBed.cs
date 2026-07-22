using Godot;

namespace Mortz.Shared.Scenes.MainMenu;

[Tool]
public partial class ConveyorChainBed : MultiMeshInstance3D
{
    [Export] private PackedScene _chainScene = null!;
    [Export] private int _columns = 14;
    [Export] private int _rows = 5;
    [Export] private float _length = 10.4f;
    [Export] private float _width = 1.35f;
    [Export] private float _chainScale = 0.075f;

    private MultiMesh? _instances;
    private float _offset;

    public override void _Ready()
    {
        Node sourceRoot = _chainScene.Instantiate();
        MeshInstance3D? source = FindMesh(sourceRoot);
        if (source?.Mesh is null)
        {
            sourceRoot.Free();
            GD.PushError("Conveyor chain scene has no MeshInstance3D.");
            return;
        }

        _instances = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = source.Mesh,
            InstanceCount = _columns * _rows,
        };
        Multimesh = _instances;
        sourceRoot.Free();
        UpdateTransforms();
    }

    public void Advance(float distance)
    {
        if (_instances is null)
            return;

        float spacing = _length / _columns;
        _offset = Mathf.PosMod(_offset + distance, spacing);
        UpdateTransforms();
    }

    private void UpdateTransforms()
    {
        if (_instances is null)
            return;

        float spacing = _length / _columns;
        float halfLength = _length * 0.5f;
        Basis basis = new Basis(Vector3.Up, Mathf.Pi * 0.5f).Scaled(Vector3.One * _chainScale);

        int index = 0;
        for (int row = 0; row < _rows; row++)
        {
            float rowRatio = _rows == 1 ? 0.5f : (float)row / (_rows - 1);
            float z = Mathf.Lerp(-_width * 0.5f, _width * 0.5f, rowRatio);
            float stagger = row % 2 == 0 ? 0.0f : spacing * 0.5f;

            for (int column = 0; column < _columns; column++)
            {
                float x = -halfLength + column * spacing + _offset + stagger;
                if (x >= halfLength)
                    x -= _length;

                _instances.SetInstanceTransform(
                    index++,
                    new Transform3D(basis, new Vector3(x, 0.0f, z)));
            }
        }
    }

    private static MeshInstance3D? FindMesh(Node node)
    {
        if (node is MeshInstance3D meshInstance)
            return meshInstance;

        foreach (Node child in node.GetChildren())
        {
            MeshInstance3D? result = FindMesh(child);
            if (result is not null)
                return result;
        }

        return null;
    }
}
