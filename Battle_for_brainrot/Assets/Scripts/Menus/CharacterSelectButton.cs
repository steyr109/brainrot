using UnityEngine;
using UnityEngine.SceneManagement;

public class CharacterSelectButton : MonoBehaviour
{
    public CharacterData character;

    public void SelectCharacter()
    {
        CharacterSelection.SelectedCharacter = character;

        if (character != null)
        {
            PlayerPrefs.SetString("SelectedCharacter", character.characterName);
        }

        SceneManager.LoadScene("Scenes/DevicesScreen");
    }
}
