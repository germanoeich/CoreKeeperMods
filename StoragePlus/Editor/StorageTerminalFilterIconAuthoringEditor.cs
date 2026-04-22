using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(StorageTerminalFilterIconAuthoring))]
public sealed class StorageTerminalFilterIconAuthoringEditor : Editor
{
    private static readonly (string PropertyName, string Label)[] CategoryFields =
    {
        ("foodIconObject", "Food"),
        ("ingredientsIconObject", "Ingredients"),
        ("cropsIconObject", "Crops"),
        ("fishIconObject", "Fish"),
        ("materialsIconObject", "Materials"),
        ("blocksIconObject", "Blocks"),
        ("groundIconObject", "Ground"),
        ("floorIconObject", "Floor"),
        ("bridgesIconObject", "Bridges"),
        ("decorationIconObject", "Decoration"),
        ("placeablesIconObject", "Placeables"),
        ("weaponsIconObject", "Weapons"),
        ("toolsIconObject", "Tools"),
        ("armorIconObject", "Armor"),
        ("accessoriesIconObject", "Accessories"),
        ("valuablesIconObject", "Valuables"),
        ("petsIconObject", "Pets"),
        ("crittersIconObject", "Critters"),
        ("miscIconObject", "Misc")
    };

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawProperty("iconContainer");
        DrawProperty("iconRenderer");

        EditorGUILayout.Space();
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Category Icons", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Each field picks the item whose full icon will represent that category.");

            EditorGUI.indentLevel++;
            for (int i = 0; i < CategoryFields.Length; i++)
            {
                DrawLabeledPicker(CategoryFields[i].PropertyName, CategoryFields[i].Label);
            }
            EditorGUI.indentLevel--;
        }

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawProperty(string propertyName)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null)
        {
            EditorGUILayout.PropertyField(property);
        }
    }

    private void DrawLabeledPicker(string propertyName, string label)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null)
        {
            return;
        }

        Rect rect = EditorGUILayout.GetControlRect();
        Rect labelRect = rect;
        labelRect.width = EditorGUIUtility.labelWidth;
        EditorGUI.LabelField(labelRect, label);

        Rect fieldRect = rect;
        fieldRect.xMin = labelRect.xMax;
        EditorGUI.PropertyField(fieldRect, property, GUIContent.none, includeChildren: true);
    }
}
