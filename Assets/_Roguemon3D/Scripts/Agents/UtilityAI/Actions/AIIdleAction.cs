using System;
using UnityEngine;

namespace UtilityAI {
    [Serializable]
    public class AIIdleAction : AIAction {
        public override void Execute(Context context) {
            //context.agent.SetDestination(context.agent.transform.position);
        }

        public override void OnExit(Context context)
        {
            // No operation needed for idle action exit
        }
    }
}