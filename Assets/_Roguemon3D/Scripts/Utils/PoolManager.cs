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
                pools = new Dictionary<Type,  Stack<MonoBehaviour>>();
            }
            else
            {
                Destroy(gameObject);
            }
        }
        
        private Dictionary<Type, Stack<MonoBehaviour>> pools = new();

        public T Spawn<T>(T prefab,  Vector3 position = default, Vector3 rotation = default, Transform parent = null) where T : MonoBehaviour
        {
            if (!pools.TryGetValue(typeof(T), out var stack))
            {
                stack = new Stack<MonoBehaviour>();
                pools[typeof(T)] = stack;
            }
            T inst = (stack.Count > 0 ? stack.Pop() : Instantiate(prefab)) as T;
            if (inst == null)
                throw new Exception("PoolManager: Spawn failed for type " + typeof(T).Name);
            inst.transform.SetParent(parent);
            inst.transform.SetPositionAndRotation(position, Quaternion.Euler(rotation));
            inst.gameObject.SetActive(true);
            return inst;
        }

        public void Despawn<T>(MonoBehaviour b)
        {
            if (!pools.TryGetValue(typeof(T), out var stack))
            {
                stack = new Stack<MonoBehaviour>();
                pools[typeof(T)] = stack;
            }
            b.gameObject.SetActive(false);
            stack.Push(b);
            b.transform.SetParent(transform);
        }
    }
}