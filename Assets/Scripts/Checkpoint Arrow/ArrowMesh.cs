using UnityEngine;

[ExecuteInEditMode]
public class ArrowMesh : MonoBehaviour
{
    public float stickLength = 1f;
    public float stickRadius = 0.05f;
    public float tipLength = 0.4f;
    public float tipRadius = 0.15f;
    public int segments = 12;
    public Color color = Color.yellow;

    private void OnEnable() => BuildArrow();

    private void BuildArrow()
    {
        foreach (Transform child in transform)
            DestroyImmediate(child.gameObject);

        BuildStick();
        BuildTip();
    }

    private void BuildStick()
    {
        var go = new GameObject("Stick");
        go.transform.SetParent(transform);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        mf.mesh = CreateCylinder(stickRadius, stickLength, segments);
        mr.sharedMaterial = CreateMat();
    }

    private void BuildTip()
    {
        var go = new GameObject("Tip");
        go.transform.SetParent(transform);
        go.transform.localPosition = new Vector3(0f, stickLength, 0f);
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        mf.mesh = CreateCone(tipRadius, tipLength, segments);
        mr.sharedMaterial = CreateMat();
    }

    private Mesh CreateCylinder(float radius, float length, int segs)
    {
        Mesh mesh = new Mesh();
        mesh.name = "Cylinder";

        Vector3[] verts = new Vector3[segs * 2 + 2];
        int[] tris = new int[segs * 12];

        // Cercles bas et haut
        for (int i = 0; i < segs; i++)
        {
            float angle = 2 * Mathf.PI * i / segs;
            float x = Mathf.Cos(angle) * radius;
            float z = Mathf.Sin(angle) * radius;
            verts[i]        = new Vector3(x, 0,      z);
            verts[i + segs] = new Vector3(x, length, z);
        }

        // Centres
        verts[segs * 2]     = new Vector3(0, 0,      0);
        verts[segs * 2 + 1] = new Vector3(0, length, 0);

        int t = 0;

        for (int i = 0; i < segs; i++)
        {
            int next = (i + 1) % segs;

            // Flanc
            tris[t++] = i;
            tris[t++] = i + segs;
            tris[t++] = next + segs;

            tris[t++] = i;
            tris[t++] = next + segs;
            tris[t++] = next;

            // Cap bas
            tris[t++] = segs * 2;
            tris[t++] = next;
            tris[t++] = i;

            // Cap haut
            tris[t++] = segs * 2 + 1;
            tris[t++] = i + segs;
            tris[t++] = next + segs;
        }

        mesh.vertices  = verts;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        return mesh;
    }

    private Mesh CreateCone(float radius, float length, int segs)
    {
        Mesh mesh = new Mesh();
        mesh.name = "Cone";

        Vector3[] verts = new Vector3[segs + 2];
        int[] tris = new int[segs * 6];

        // Cercle base
        for (int i = 0; i < segs; i++)
        {
            float angle = 2 * Mathf.PI * i / segs;
            verts[i] = new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
        }

        // Pointe + centre base
        verts[segs]     = new Vector3(0, length, 0); // pointe
        verts[segs + 1] = Vector3.zero;              // centre base

        int t = 0;

        for (int i = 0; i < segs; i++)
        {
            int next = (i + 1) % segs;

            // Flanc vers la pointe
            tris[t++] = i;
            tris[t++] = segs;
            tris[t++] = next;

            // Cap base
            tris[t++] = segs + 1;
            tris[t++] = next;
            tris[t++] = i;
        }

        mesh.vertices  = verts;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        return mesh;
    }

    private Material CreateMat()
    {
        var mat = new Material(Shader.Find("Standard"));
        mat.color = color;
        return mat;
    }
}