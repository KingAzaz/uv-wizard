using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// UVWizard is a utility class for combining multiple materials into a single texture atlas
/// and adjusting the UVs of the _mesh to map correctly to the atlas.
/// </summary>
public class UVWizard : MonoBehaviour
{
    /// <summary>
    /// The size of the combined texture atlas.
    /// </summary>
    public int textureAtlasSize = 2048;

    private Mesh _mesh;
    private MeshRenderer _meshRenderer;
    private Material[] _originalMaterials;
    private Rect[] _uvRects;
    private Texture2D _textureAtlas;
    private Material _newMaterial;

    private void Start()
    {
        CombineMaterials();
    }

    /// <summary>
    /// Main method to execute the combination process.
    /// </summary>
    public void CombineMaterials()
    {
        GetMeshAndMaterials();

        if (_mesh == null || _originalMaterials == null)
        {
            Debug.LogError("Failed to get _mesh or materials.");
            return;
        }

        CreateTextureAtlas();
        RepackUVs();
        CreateNewMaterial();
        ApplyNewMaterial();

        Debug.Log("Materials combined successfully!");
    }

    /// <summary>
    /// Retrieves and duplicates the _mesh and materials.
    /// </summary>
    private void GetMeshAndMaterials()
    {
        MeshFilter meshFilter = GetComponent<MeshFilter>();
        _meshRenderer = GetComponent<MeshRenderer>();

        if (meshFilter == null || _meshRenderer == null)
        {
            Debug.LogError("UVWizard requires a MeshFilter and MeshRenderer.");
            return;
        }

        // Duplicate the _mesh to avoid modifying the original asset
        _mesh = Instantiate(meshFilter.sharedMesh);
        meshFilter.mesh = _mesh;

        _originalMaterials = _meshRenderer.sharedMaterials;
    }

    /// <summary>
    /// Packs the textures into a texture atlas.
    /// </summary>
    private void CreateTextureAtlas()
    {
        int count = _originalMaterials.Length;
        Texture2D[] textures = new Texture2D[count];

        // Collect textures from materials
        for (int i = 0; i < count; i++)
        {
            Texture2D tex = (Texture2D)_originalMaterials[i].mainTexture;

            if (tex == null)
            {
                tex = Texture2D.whiteTexture;
            }

            // Ensure texture is readable
            if (!tex.isReadable)
            {
                Debug.LogWarning($"Texture '{tex.name}' is not readable. Setting it to readable.");
                SetTextureReadable(tex);
            }

            textures[i] = tex;
        }

        // Create the atlas
        _textureAtlas = new Texture2D(textureAtlasSize, textureAtlasSize);
        _uvRects = _textureAtlas.PackTextures(textures, 0, textureAtlasSize);
    }

    /// <summary>
    /// Adjusts the UVs to map correctly to the texture atlas.
    /// </summary>
    private void RepackUVs()
    {
        Vector2[] uvs = _mesh.uv;

        for (int i = 0; i < _mesh.subMeshCount; i++)
        {
            int[] triangles = _mesh.GetTriangles(i);
            Rect rect = _uvRects[i];

            foreach (int index in triangles)
            {
                Vector2 uv = uvs[index];

                // Scale and offset UVs to fit in the assigned rectangle
                uv = new Vector2(
                    rect.x + uv.x * rect.width,
                    rect.y + uv.y * rect.height
                );

                uvs[index] = uv;
            }
        }

        _mesh.uv = uvs;
    }

    /// <summary>
    /// Creates a new material with the combined texture.
    /// </summary>
    private void CreateNewMaterial()
    {
        _newMaterial = new Material(Shader.Find("Standard"));
        _newMaterial.mainTexture = _textureAtlas;
    }

    /// <summary>
    /// Applies the new material to the _mesh.
    /// </summary>
    private void ApplyNewMaterial()
    {
        _meshRenderer.material = _newMaterial;
    }

    /// <summary>
    /// Makes a texture readable (editor-only).
    /// </summary>
    /// <param name="tex">The texture to make readable.</param>
    private void SetTextureReadable(Texture2D tex)
    {
#if UNITY_EDITOR
        string assetPath = UnityEditor.AssetDatabase.GetAssetPath(tex);
        var textureImporter = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(assetPath);
        textureImporter.isReadable = true;
        UnityEditor.AssetDatabase.ImportAsset(assetPath);
#else
        Debug.LogError("Cannot set texture to readable at runtime.");
#endif
    }
}
