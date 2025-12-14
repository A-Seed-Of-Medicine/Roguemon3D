using System;
using UnityEngine;
using Unity.Properties;

namespace UtilityAI
{
    [Serializable]
    public sealed class PropertyConsideration : Consideration
    {
        [Tooltip("Any UnityEngine.Object to read from (MonoBehaviour, ScriptableObject, etc).")]
        [SerializeField] private UnityEngine.Object source;

        [Tooltip("Property path within the selected object. Populated via the drawer.")]
        [SerializeField] private string propertyPath;

        [NonSerialized] private bool _pathCached;
        [NonSerialized] private PropertyPath _cachedPath;

        public override float Evaluate(Context context, TargetContext target)
        {
            if (source == null || string.IsNullOrEmpty(propertyPath))
                return 0f;

            if (!_pathCached)
            {
                _cachedPath = new PropertyPath(propertyPath); // cache once
                _pathCached = true;
            }

            object container = source; // generic entry point; PropertyContainer resolves bag by runtime type

            // Try float
            if (PropertyContainer.TryGetValue(ref container, _cachedPath, out float fVal))
                return fVal;

            // Try int -> float
            if (PropertyContainer.TryGetValue(ref container, _cachedPath, out int iVal))
                return iVal;

            return 0f;
        }
    }
}