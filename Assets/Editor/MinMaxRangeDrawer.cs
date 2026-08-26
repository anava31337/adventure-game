// =============================================================================
// MinMaxRangeDrawer.cs   —   Assets/Editor/
//
// Draws a MinMaxRange as a single line:   [ Label ]  Min [____]  Max [____]
//
// MUST live in an "Editor" folder. It references the UnityEditor namespace,
// which cannot be compiled into a build — Unity excludes anything under an
// Editor folder automatically, so your Windows/Mac/WebGL builds stay clean.
// =============================================================================

using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(MinMaxRange))]
public class MinMaxRangeDrawer : PropertyDrawer
{
    private const float LabelW = 30f;   // width of the "Min" / "Max" captions
    private const float Gap    = 6f;    // gap between the two field pairs

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        // Main field label on the left; the two number fields share the rest.
        Rect fieldArea = EditorGUI.PrefixLabel(
            position, GUIUtility.GetControlID(FocusType.Passive), label);

        // Zero the indent so the sub-fields don't get pushed off to the right.
        int indent = EditorGUI.indentLevel;
        EditorGUI.indentLevel = 0;

        float half      = (fieldArea.width - Gap) * 0.5f;
        var   minLabelR = new Rect(fieldArea.x, fieldArea.y, LabelW, fieldArea.height);
        var   minFieldR = new Rect(fieldArea.x + LabelW, fieldArea.y, half - LabelW, fieldArea.height);
        var   maxLabelR = new Rect(fieldArea.x + half + Gap, fieldArea.y, LabelW, fieldArea.height);
        var   maxFieldR = new Rect(fieldArea.x + half + Gap + LabelW, fieldArea.y, half - LabelW, fieldArea.height);

        SerializedProperty minProp = property.FindPropertyRelative("min");
        SerializedProperty maxProp = property.FindPropertyRelative("max");

        EditorGUI.LabelField(minLabelR, "Min");
        minProp.floatValue = EditorGUI.FloatField(minFieldR, minProp.floatValue);
        EditorGUI.LabelField(maxLabelR, "Max");
        maxProp.floatValue = EditorGUI.FloatField(maxFieldR, maxProp.floatValue);

        // Keep the range sane: max never drops below min.
        if (maxProp.floatValue < minProp.floatValue)
            maxProp.floatValue = minProp.floatValue;

        EditorGUI.indentLevel = indent;
        EditorGUI.EndProperty();
    }
}
