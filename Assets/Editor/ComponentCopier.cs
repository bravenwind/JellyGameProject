using UnityEngine;
using UnityEditor;

public class ComponentCopier : EditorWindow
{
    static Component[] copiedComponents;

    [MenuItem("GameObject/Copy All Components", false, 0)]
    static void Copy()
    {
        if (Selection.activeGameObject == null) return;
        copiedComponents = Selection.activeGameObject.GetComponents<Component>();
    }

    [MenuItem("GameObject/Paste All Components", false, 0)]
    static void Paste()
    {
        if (Selection.activeGameObject == null || copiedComponents == null) return;
        foreach (var comp in copiedComponents)
        {
            if (comp is Transform) continue; // Transform¿∫ ¡¶ø‹
            UnityEditorInternal.ComponentUtility.CopyComponent(comp);
            UnityEditorInternal.ComponentUtility.PasteComponentAsNew(Selection.activeGameObject);
        }
    }
}