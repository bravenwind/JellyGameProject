using UnityEngine;

public class JellyMesh : MonoBehaviour
{
    public float intensity = 1f;
    public float mass = 1f;
    public float stiffness = 1f;
    public float damping = 0.75f;
    private Mesh originalMesh, meshClone;
    private MeshRenderer meshRenderer;
    private JellyVertex[] jv;
    private Vector3[] vertexArray;

    private void Start()
    {
        originalMesh = GetComponent<MeshFilter>().sharedMesh;
        meshClone = Instantiate(originalMesh);
        GetComponent<MeshFilter>().sharedMesh = meshClone;
        meshRenderer = GetComponent<MeshRenderer>();
        jv = new JellyVertex[meshClone.vertices.Length];
        for (int i = 0; i < meshClone.vertices.Length; i++)
            jv[i] = new JellyVertex(i, transform.TransformPoint(meshClone.vertices[i]));
    }

    private void FixedUpdate()
    {
        vertexArray = originalMesh.vertices;
        for (int i = 0; i < jv.Length;i++)
        {
            Vector3 target = transform.TransformPoint(vertexArray[jv[i].id]);   
            float tempIntensity = (1 - (meshRenderer.bounds.max.y  - target.y) / meshRenderer.bounds.size.y) * intensity;
            jv[i].Shake(target, mass, stiffness, damping);
            target = transform.InverseTransformPoint(jv[i].position);
            vertexArray[jv[i].id] = Vector3.Lerp(vertexArray[jv[i].id], target, tempIntensity);
        }
        meshClone.vertices = vertexArray;   
    }

    public class JellyVertex
    {
        public int id;
        public Vector3 position;
        public Vector3 velocity, force;

        public JellyVertex(int _id, Vector3 _pos)
        {
            id = _id;
            position = _pos;
        }

        public void Shake(Vector3 target, float m, float s, float d)
        {
            force = (target - position) * s;
            velocity = (velocity + force / m) * d;
            position += velocity;
            if ((velocity + force + force / m).magnitude < 0.001f)
                position = target;
        }
    }
}
