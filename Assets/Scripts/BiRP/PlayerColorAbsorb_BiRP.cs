using UnityEngine;
using System.Collections;

public class PlayerColorAbsorb_BiRP : MonoBehaviour
{
    public Renderer rend;
    private Color currentColor;
    public Rigidbody[] rigidbodies;
    public ColorUI colorUI;

    void Start()
    {
        currentColor = rend.material.color;
    }

    void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag("Object"))
        {
            foreach (Rigidbody rigidbody in rigidbodies)
            {
                rigidbody.constraints = RigidbodyConstraints.None;
            }
        }

        if (collision.gameObject.CompareTag("Edible"))
        {
            collision.rigidbody.useGravity = false;
            collision.collider.isTrigger = true;
        }
    }

    public void AbsorbColor(Color jellyColor)
    {
        Color targetColor = Color.Lerp(currentColor, jellyColor, 0.6f);
        StartCoroutine(BlendColor(targetColor, 0.25f));
    }

    IEnumerator BlendColor(Color target, float time)
    {
        Color start = currentColor;
        float t = 0f;

        while (t < time)
        {
            t += Time.deltaTime;
            currentColor = Color.Lerp(start, target, t / time);
            rend.material.color = currentColor;
            yield return null;
        }

        currentColor = target;
        rend.material.color = currentColor;
        colorUI.ChangeColor(currentColor);
    }
}
