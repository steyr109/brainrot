using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

public static class BrainrotUiInput
{
    public static void EnsureEventSystem()
    {
        EventSystem eventSystem = Object.FindFirstObjectByType<EventSystem>();
        GameObject eventSystemObject;

        if (eventSystem == null)
        {
            eventSystemObject = new GameObject("EventSystem");
            eventSystem = eventSystemObject.AddComponent<EventSystem>();
        }
        else
        {
            eventSystemObject = eventSystem.gameObject;
        }

        foreach (StandaloneInputModule legacyModule in eventSystemObject.GetComponents<StandaloneInputModule>())
            Object.Destroy(legacyModule);

        InputSystemUIInputModule inputModule = eventSystemObject.GetComponent<InputSystemUIInputModule>();
        if (inputModule == null)
            inputModule = eventSystemObject.AddComponent<InputSystemUIInputModule>();

        inputModule.AssignDefaultActions();
        eventSystem.enabled = true;
        inputModule.enabled = true;
    }
}
