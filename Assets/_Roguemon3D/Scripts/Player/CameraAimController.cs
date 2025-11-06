using _PinBoy.Scripts.CharacterMovement;
using UnityEngine;

public class CameraAimController : MonoBehaviour
{
    public float distanceScale = 1f;
    public AgentController agentController;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        // Set transform position ahead of agent transform relative to agentCharacterController velocity
        if (agentController && agentController.rb)
        {
            Vector3 velocity = agentController.rb.linearVelocity;
            Vector3 planarOffset = new Vector3(velocity.x, 0f, velocity.z) * distanceScale;
            Vector3 basePosition = agentController.transform.position + planarOffset;
            transform.position = new Vector3(basePosition.x, transform.position.y, basePosition.z);
        }
    }
}
