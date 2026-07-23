using System.Collections.Generic;
using Godot;

namespace Mortz.Shared.Rendering;

/// <summary>Swaps the baked GLB materials in the subtree for the PSX retro shaders, carrying each surface's albedo across.</summary>
[Tool]
public partial class PsxMaterializer : Node
{
    [Export] private Shader _texturedShader = null!;
    [Export] private Shader _untexturedShader = null!;
    [Export] private Shader _transparentShader = null!;
    [Export] private Shader _spriteShader = null!;
    [Export] private Node3D? _root;

    private const string IGNORE_AUTO_SHADER = "IGNORE_AUTO_SHADER";
    private const string TRANSPARENT_AUTO_SHADER = "TRANSPARENT_AUTO_SHADER";

    private readonly List<AnimatedSprite3D> _animatedSprites = [];

    private bool _previewInEditor;
    private bool _refreshQueued;

    [Export]
    public bool PreviewInEditor
    {
        get => _previewInEditor;
        set
        {
            if (_previewInEditor == value)
                return;

            _previewInEditor = value;
            if (Engine.IsEditorHint())
                QueueRefresh();
        }
    }

    public override void _Ready()
    {
        if (Engine.IsEditorHint())
        {
            SceneTree tree = GetTree();
            tree.NodeAdded += OnSceneChanged;
            tree.NodeRemoved += OnSceneChanged;
            if (_previewInEditor)
                QueueRefresh();
            return;
        }

        // deferred so runtime-built meshes (the chain MultiMesh) exist first
        CallDeferred(nameof(Apply));
    }

    public override void _ExitTree()
    {
        if (!Engine.IsEditorHint())
            return;

        SceneTree? tree = GetTree();
        if (tree is null)
            return;

        tree.NodeAdded -= OnSceneChanged;
        tree.NodeRemoved -= OnSceneChanged;
    }

    public override void _Process(double delta)
    {
        if (_animatedSprites.Count == 0)
            return;

        for (int i = _animatedSprites.Count - 1; i >= 0; i--)
        {
            AnimatedSprite3D sprite = _animatedSprites[i];
            if (!IsInstanceValid(sprite) || sprite.MaterialOverride is not ShaderMaterial material)
            {
                _animatedSprites.RemoveAt(i);
                continue;
            }

            material.SetShaderParameter("albedo_texture", CurrentSpriteTexture(sprite));
        }
    }

    public void Apply()
    {
        Node3D? root = _root ?? GetParent() as Node3D;
        if (root is not null)
            Convert(root, transparent: false);
    }

    private void OnSceneChanged(Node node)
    {
        if (!_previewInEditor)
            return;

        Node3D? root = _root ?? GetParent() as Node3D;
        if (root is not null && (node == root || root.IsAncestorOf(node)))
            QueueRefresh();
    }

    private void QueueRefresh()
    {
        if (_refreshQueued || !IsInsideTree())
            return;

        _refreshQueued = true;
        CallDeferred(nameof(RefreshPreview));
    }

    private void RefreshPreview()
    {
        _refreshQueued = false;

        Node3D? root = _root ?? GetParent() as Node3D;
        if (root is null)
            return;

        if (_previewInEditor)
            Convert(root, transparent: false);
        else
            Restore(root);
    }

    private void Convert(Node node, bool transparent)
    {
        if (node.IsInGroup(IGNORE_AUTO_SHADER))
            return;

        transparent |= node.IsInGroup(TRANSPARENT_AUTO_SHADER);

        switch (node)
        {
            case MultiMeshInstance3D multi:
                ConvertMultiMesh(multi, transparent);
                break;
            case MeshInstance3D mesh:
                ConvertMesh(mesh, transparent);
                break;
            case SpriteBase3D sprite:
                ConvertSprite(sprite);
                break;
        }

        foreach (Node child in node.GetChildren())
        {
            Convert(child, transparent);
        }
    }

    private void Restore(Node node)
    {
        if (node.IsInGroup(IGNORE_AUTO_SHADER))
            return;

        switch (node)
        {
            case MultiMeshInstance3D multi when IsRetro(multi.MaterialOverride):
                multi.MaterialOverride = null;
                break;
            case MeshInstance3D mesh:
                RestoreMesh(mesh);
                break;
            case SpriteBase3D sprite when IsRetro(sprite.MaterialOverride):
                sprite.MaterialOverride = null;
                if (sprite is AnimatedSprite3D animated)
                    _animatedSprites.Remove(animated);
                break;
        }

        foreach (Node child in node.GetChildren())
        {
            Restore(child);
        }
    }

    private void ConvertMesh(MeshInstance3D instance, bool transparent)
    {
        if (instance.Mesh is null)
            return;

        for (int surface = 0; surface < instance.Mesh.GetSurfaceCount(); surface++)
        {
            if (IsRetro(instance.GetActiveMaterial(surface)))
                continue;

            instance.SetSurfaceOverrideMaterial(
                surface,
                BuildMaterial(instance.GetActiveMaterial(surface), transparent));
        }
    }

    private void RestoreMesh(MeshInstance3D instance)
    {
        if (instance.Mesh is null)
            return;

        for (int surface = 0; surface < instance.Mesh.GetSurfaceCount(); surface++)
        {
            if (IsRetro(instance.GetSurfaceOverrideMaterial(surface)))
                instance.SetSurfaceOverrideMaterial(surface, null);
        }
    }

    private void ConvertMultiMesh(MultiMeshInstance3D instance, bool transparent)
    {
        if (IsRetro(instance.MaterialOverride))
            return;

        Mesh? mesh = instance.Multimesh?.Mesh;
        if (mesh is null || mesh.GetSurfaceCount() == 0)
            return;

        instance.MaterialOverride = BuildMaterial(mesh.SurfaceGetMaterial(0), transparent);
    }

    private void ConvertSprite(SpriteBase3D instance)
    {
        if (IsRetro(instance.MaterialOverride))
            return;

        ShaderMaterial material = new() { Shader = _spriteShader };
        material.SetShaderParameter("albedo_texture", CurrentSpriteTexture(instance));
        material.SetShaderParameter("billboard_mode", (int)instance.Billboard);
        instance.MaterialOverride = material;

        if (instance is AnimatedSprite3D animated)
            _animatedSprites.Add(animated);
    }

    private static Texture2D? CurrentSpriteTexture(SpriteBase3D sprite) =>
        sprite switch
        {
            Sprite3D still => still.Texture,
            AnimatedSprite3D animated =>
                animated.SpriteFrames?.GetFrameTexture(animated.Animation, animated.Frame),
            _ => null,
        };

    private ShaderMaterial BuildMaterial(Material? source, bool transparent)
    {
        Texture2D? albedo = (source as BaseMaterial3D)?.AlbedoTexture;
        ShaderMaterial material = new() { Shader = PickShader(albedo, transparent) };
        if (albedo is not null)
            material.SetShaderParameter("albedo_texture", albedo);

        return material;
    }

    private Shader PickShader(Texture2D? albedo, bool transparent)
    {
        if (albedo is null)
            return _untexturedShader;

        return transparent ? _transparentShader : _texturedShader;
    }

    private bool IsRetro(Material? material) =>
        material is ShaderMaterial shader &&
        (shader.Shader == _texturedShader ||
            shader.Shader == _untexturedShader ||
            shader.Shader == _transparentShader ||
            shader.Shader == _spriteShader);
}
