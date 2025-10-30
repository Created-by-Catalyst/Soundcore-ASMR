using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class GlobalManager : MonoBehaviour
{

    [SerializeField]
    int pageNum = 0;

    [SerializeField]
    CanvasGroup[] pages;

    [SerializeField]
    AudioHandler audioHandler;


    [SerializeField]
    Slider loadingBar;

    [SerializeField]
    TMP_Text qrTimerText;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        foreach (var page in pages)
        {
            page.alpha = 0;
        }


        pages[0].alpha = 1;
    }

    public void NextPage()
    {
        StartCoroutine(TransitionToNextPage());
    }


    float transitionSpeed = 1f;

    IEnumerator TransitionToNextPage()
    {
        pages[pageNum].interactable = false;
        pages[pageNum].blocksRaycasts = false;

        while (pages[pageNum].alpha > 0)
        {
            pages[pageNum].alpha -= Time.deltaTime * transitionSpeed;
            yield return null;
        }

        pageNum++;

        while (pages[pageNum].alpha < 1)
        {
            pages[pageNum].alpha += Time.deltaTime * transitionSpeed;
            yield return null;
        }


        pages[pageNum].interactable = true;
        pages[pageNum].blocksRaycasts = true;


        switch (pageNum)
        {
            case 2:
                audioHandler.StartRecording();
                break;
            case 4:
                StartCoroutine(LoadSound());
                break;
            case 5:
                yield return new WaitForSeconds(5);
                NextPage();
                break;
            case 6:
                StartCoroutine(QRTimer());
                break;
            case 7:
                yield return new WaitForSeconds(6);
                SceneManager.LoadScene(0);
                break;
            default:
                break;
        }
    }


    IEnumerator QRTimer()
    {
        float timerProgress = 20;

        while (timerProgress > 0)
        {
            timerProgress -= Time.deltaTime;
            qrTimerText.text = $"00:{(timerProgress):00}";
            yield return null;
        }

        qrTimerText.text = "00:00";

        NextPage();
    }

IEnumerator LoadSound()
    {

        float loadingProgress = 0;

        float loadingSpeed = 0.2f;

        while (loadingProgress < 1)
        {
            loadingProgress += Time.deltaTime * loadingSpeed;

            if (loadingProgress > 0.3f && loadingProgress < 0.85f) loadingSpeed = 0.5f;

            if (loadingProgress > 0.85f) loadingSpeed = 0.15f;

            loadingBar.value = loadingProgress;
            yield return null;
        }


        loadingBar.value = 1;

        NextPage();
    }

}
