using System;
using System.Collections;
using Arkon.Unity;
using Arkon.Unity.Intro;
using WhiteGuard.Gameplay;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WhiteGuard.Presentation
{
    /// <summary>Entry point for the complete standalone campaign, preserving the original opening player.</summary>
    [DefaultExecutionOrder(500)]
    public sealed class WhiteGuardGameBootstrap : MonoBehaviour
    {
        private CampaignGameLoop loop;
        private CampaignUI ui;
        private CampaignArt art;
        private GameObject introHost;
        private IntroPlayer intro;
        private bool startAfterIntro;
        private Coroutine introLoading;
        private void Awake()
        {
            ui = gameObject.AddComponent<CampaignUI>();
            try
            {
                Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
                var camera = SceneObjects.Find<Camera>(this);
                if (!camera)
                {
                    var value = new GameObject("Campaign Camera"); SceneManager.MoveGameObjectToScene(value, gameObject.scene);
                    camera = value.AddComponent<Camera>(); camera.tag = "MainCamera";
                }
                if (!SceneObjects.Find<AudioListener>(this)) camera.gameObject.AddComponent<AudioListener>();
                var rig = camera.GetComponent<CampaignCamera>(); if (!rig) rig = camera.gameObject.AddComponent<CampaignCamera>();
                art = new CampaignArt(); loop = gameObject.AddComponent<CampaignGameLoop>();
                var presentation = gameObject.AddComponent<CampaignPresentation>(); presentation.Configure(loop, art);
                var data = Resources.Load<TextAsset>("WhiteGuardCampaign/campaign");
                if (!data) throw new InvalidOperationException("Не найдены данные кампании.");
                loop.Configure(data, camera); rig.Loop = loop;
                ui.Configure(loop, art, data, ShowOpening, () => introHost && introHost.activeSelf);
                gameObject.AddComponent<CampaignAudio>().Configure(loop, () => introHost && introHost.activeSelf);
                introHost = new GameObject("Original White Guard opening"); introHost.SetActive(false);
                introHost.transform.SetParent(transform, false); intro = introHost.AddComponent<IntroPlayer>();
                intro.Finished += IntroFinished;
            }
            catch (Exception error)
            {
                Debug.LogException(error, this);
                ui.SetStartupError("Не удалось загрузить файлы игры. Проверьте целостность установки и перезапустите её.");
            }
        }
        private void ShowOpening(bool beginCampaign)
        {
            if (!loop || !introHost) return;
            if (beginCampaign && PlayerPrefs.GetInt("WhiteGuard.OpeningSeen", 0) != 0) { loop.StartNew(); return; }
            loop.ReturnToMenu(); startAfterIntro = beginCampaign;
            introHost.SetActive(true);
            if (introLoading != null) StopCoroutine(introLoading);
            introLoading = StartCoroutine(WaitForIntro());
        }
        private IEnumerator WaitForIntro()
        {
            while (intro && !intro.Ready && intro.State != IntroPlaybackState.Error) yield return null;
            introLoading = null;
            if (intro && intro.Ready) intro.StartPlayback();
        }
        private void IntroFinished(bool skipped)
        {
            var next = startAfterIntro; startAfterIntro = false;
            PlayerPrefs.SetInt("WhiteGuard.OpeningSeen", 1); PlayerPrefs.Save();
            introHost.SetActive(false);
            if (next) loop.StartNew();
        }
        private void Update()
        {
            if (introHost && introHost.activeSelf && intro && Input.GetKeyDown(KeyCode.Escape)
                && (intro.State == IntroPlaybackState.Title || intro.State == IntroPlaybackState.Error)) LeaveOpening();
        }
        private void OnGUI()
        {
            if (!introHost || !introHost.activeSelf || !intro || (intro.State == IntroPlaybackState.Playing && !intro.Paused)) return;
            if (GUI.Button(new Rect(22, 22, 138, 38), "←  В меню")) LeaveOpening();
        }
        private void LeaveOpening()
        {
            if (introLoading != null) { StopCoroutine(introLoading); introLoading = null; }
            startAfterIntro = false; introHost.SetActive(false); loop.ReturnToMenu();
        }
        private void OnDestroy()
        {
            if (intro) intro.Finished -= IntroFinished;
            if (introLoading != null) StopCoroutine(introLoading);
            art?.Dispose();
        }
    }
}
