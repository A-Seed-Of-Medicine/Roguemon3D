#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace UtilityAI.Editor
{
    [CustomPropertyDrawer(typeof(Consideration), true)]
    public class ConsiderationDrawer : ManagedReferencePropertyDrawer
    {
        protected override Type BaseType => typeof(Consideration);

        protected override string GetTypeDisplayName(Type type)
        {
            return base.GetTypeDisplayName(type).Replace(" Consideration", string.Empty);
        }
    }
}
#endif
