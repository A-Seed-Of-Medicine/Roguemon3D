using System;
using System.Collections.Generic;
using UnityEngine;

namespace _Roguemon3D.Scripts.Utils
{
    public class PoolManager : MonoBehaviour
    {
        public static PoolManager Instance;
        
        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
                pools = new Dictionary<String,  Stack<MonoBehaviour>>();
            }
            else
            {
                Destroy(gameObject);
            }
        }
        
        private Dictionary<String, Stack<MonoBehaviour>> pools = new();

        public T Spawn<T>(T prefab,  Vector3 position = default, Vector3 rotation = default, Transform parent = null) where T : MonoBehaviour
        {
            if (!pools.TryGetValue(prefab.name, out var stack))
            {
                stack = new Stack<MonoBehaviour>();
                pools[prefab.name] = stack;
            }
            
            T inst;
            if (stack.Count > 0)
                inst = stack.Pop() as T;
            else
                inst = Instantiate(prefab);
            
            inst.name = prefab.name + "_Instance";
            if (inst == null)
                throw new Exception("PoolManager: Spawn failed for type " + inst);
            inst.transform.SetParent(parent);
            
            if (parent)
                inst.transform.SetLocalPositionAndRotation(position, Quaternion.Euler(rotation));
            else 
                inst.transform.SetPositionAndRotation(position, Quaternion.Euler(rotation));
            inst.gameObject.SetActive(true);
            return inst;
        }

        public void Despawn(MonoBehaviour b)
        {
            string key = b.name.Replace("_Instance", "");
            
            if (!pools.TryGetValue(key, out var stack))
            {
                stack = new Stack<MonoBehaviour>();
                pools[key] = stack;
            }
            b.gameObject.SetActive(false);
            stack.Push(b);
            b.transform.SetParent(transform);
        }
    }
}