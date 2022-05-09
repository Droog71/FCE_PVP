using UnityEngine;
using Lidgren.Network;
using System;
using System.IO;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;

public class FCE_PVP : FortressCraftMod
{
    private bool usingGun;
    private static AudioClip gunSound;
    private AudioSource gunAudio;
    private Camera mCam;
    private GameObject gun;
    private Mesh gunMesh;
    private Texture2D gunTexture;
    private static ParticleSystem impactEffect;
    private static bool takingDamage;
    private ParticleSystem muzzleFlash;
    private Coroutine audioLoadingCoroutine;
    private Coroutine bulletCoroutine;
    private static readonly string assemblyFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
    private static readonly string gunModelPath = Path.Combine(assemblyFolder, "Models/gun.obj");
    private static readonly string gunTexturePath = Path.Combine(assemblyFolder, "Images/gun.png");
    private static readonly string gunAudioPath = Path.Combine(assemblyFolder, "Sounds/gun.wav");
    private UriBuilder gunTextureUriBuilder = new UriBuilder(gunTexturePath);
    private UriBuilder gunAudioUribuilder = new UriBuilder(gunAudioPath);

    public override ModRegistrationData Register()
    {
        ModRegistrationData modRegistrationData = new ModRegistrationData();
        modRegistrationData.RegisterServerComms("Maverick.FCE_PVP", ServerWrite, ClientRead);
        modRegistrationData.RegisterClientComms("Maverick.FCE_PVP", ClientWrite, ServerRead);
        return modRegistrationData;
    }

    private static void ClientWrite(BinaryWriter writer, object data)
    {
        writer.Write((ulong)data);
    }

    private static void ServerWrite(BinaryWriter writer, Player player, object data)
    {
        writer.Write((int)player.mUserID);
    }

    private static void ClientRead(NetIncomingMessage netIncomingMessage)
    {
        Player player = NetworkManager.instance.mClientThread.mPlayer;
        if (netIncomingMessage.ReadInt32() == (int)player.mUserID)
        {
            takingDamage = true;
        }
    }

    private static void ServerRead(NetIncomingMessage netIncomingMessage, Player player)
    {
        ulong target = (ulong)netIncomingMessage.ReadInt64();
        List<NetworkServerConnection> connections = NetworkManager.instance.mServerThread.connections;
        for (int i = 0; i < connections.Count; i++)
        {
            if (connections[i] != null)
            {
                if (connections[i].mState == eNetworkConnectionState.Playing)
                {
                    if (connections[i].mPlayer != null)
                    {
                        if (connections[i].mPlayer.mUserID == target)
                        {
                            ModManager.ModSendServerCommToClient("Maverick.FCE_PVP", connections[i].mPlayer);
                            break;
                        }
                    }
                }
            }
        }
    }

    public IEnumerator Start()
    {
        gunTextureUriBuilder.Scheme = "file";
        gunTexture = new Texture2D(512, 512, TextureFormat.DXT5, false);

        using (WWW www = new WWW(gunTextureUriBuilder.ToString()))
        {
            yield return www;
            www.LoadImageIntoTexture(gunTexture);
        }

        ObjImporter importer = new ObjImporter();
        gunMesh= importer.ImportFile(gunModelPath);
    }

    private IEnumerator LoadGunAudio()
    {
        gunAudioUribuilder.Scheme = "file";
        gunAudio = gun.AddComponent<AudioSource>();
        using (WWW www = new WWW(gunAudioUribuilder.ToString()))
        {
            yield return www;
            gunSound = www.GetAudioClip();
            gunAudio.clip = gunSound;
        }
    }

    private void CreateGun()
    {
        gun = GameObject.CreatePrimitive(PrimitiveType.Cube);
        gun.transform.position = mCam.gameObject.transform.position + (mCam.gameObject.transform.forward * 1);
        gun.transform.forward = mCam.transform.forward;
        gun.GetComponent<MeshFilter>().mesh = gunMesh;
        gun.GetComponent<Renderer>().material.mainTexture = gunTexture;
        audioLoadingCoroutine = StartCoroutine(LoadGunAudio());
    }

    private void ManageGun()
    {
        usingGun &= WeaponSelectScript.meActiveWeapon == eWeaponType.eNone;
        gun.GetComponent<Renderer>().enabled = usingGun;
        gun.transform.position = mCam.gameObject.transform.position + (mCam.gameObject.transform.forward * 1);
        gun.transform.forward = mCam.transform.forward;
        if (usingGun)
        {
            UIManager.UpdateUIRules("Weapon", UIRules.HideHotBar | UIRules.ShowSuitPanels | UIRules.ShowCrossHair);
        }
    }

    private void FireGun()
    {
        if (!gunAudio.isPlaying && !UIManager.CursorShown)
        {
            if (SurvivalPowerPanel.mrSuitPower >= 10)
            {
                SurvivalPowerPanel.mrSuitPower -= 10;

                gunAudio.Play();

                if (muzzleFlash == null)
                {
                    muzzleFlash = SurvivalParticleManager.instance.Raygun_Impact;
                }
                muzzleFlash.transform.position = gun.transform.position + (gun.transform.forward * 2);
                muzzleFlash.Emit(15);

                bulletCoroutine = StartCoroutine(BulletScan());
            }
        }
    }

    private IEnumerator BulletScan()
    {
        int yieldInterval = 0;
        for (int i = 0; i < 100; i++)
        {
            Vector3 bulletPos = mCam.transform.position + mCam.transform.forward * i;
            WorldScript.instance.mPlayerFrustrum.GetCoordsFromUnity(bulletPos, out long bulletX, out long bulletY, out long bulletZ);
            ushort localCube = WorldScript.instance.GetLocalCube(bulletX, bulletY, bulletZ);
           
            if (!CubeHelper.IsTypeConsideredPassable(localCube))
            {
                if (impactEffect == null)
                {
                    impactEffect = SurvivalParticleManager.instance.EMPEffect;
                }
                impactEffect.transform.position = bulletPos - mCam.transform.forward * 1;
                impactEffect.Emit(15);
                break;
            }

            if (NetworkManager.instance != null)
            {
                if (NetworkManager.instance.mClientThread != null)
                {
                    if (NetworkManager.instance.mClientThread.mOtherPlayers != null)
                    {
                        foreach (Player player in NetworkManager.instance.mClientThread.mOtherPlayers.Values)
                        {
                            float bX = bulletX - 4611686017890516992L;
                            float bY = bulletY - 4611686017890516992L;
                            float bZ = bulletZ - 4611686017890516992L; 
                            Vector3 bulletCoords = new Vector3(bX, bY, bZ);

                            float pX = player.mnWorldX - 4611686017890516992L;
                            float pY = player.mnWorldY - 4611686017890516992L;
                            float pZ = player.mnWorldZ - 4611686017890516992L;
                            Vector3 playerCoords = new Vector3(pX, pY, pZ);

                            float distance = Vector3.Distance(playerCoords, bulletCoords);
                            if (distance <= 2)
                            {
                                DamagePlayer(player, bulletPos);
                                break;
                            }
                        }
                    }
                }
            }

            yieldInterval++;
            if (yieldInterval >= 25)
            {
                yieldInterval = 0;
                yield return null;
            }
        }
    }

    private void DamagePlayer(Player player, Vector3 pos)
    {
        ModManager.ModSendClientCommToServer("Maverick.FCE_PVP", player.mUserID);

        if (impactEffect == null)
        {
            impactEffect = SurvivalParticleManager.instance.EMPEffect;
        }

        impactEffect.transform.position = pos;
        impactEffect.Emit(15);
    }

    private void TakeDamage()
    {
        AudioSource.PlayClipAtPoint(gunSound, mCam.transform.position + mCam.transform.forward * 5);

        if (impactEffect == null)
        {
            impactEffect = SurvivalParticleManager.instance.EMPEffect;
        }

        impactEffect.transform.position = mCam.transform.position;
        impactEffect.Emit(15);

        if (SurvivalPowerPanel.Hurt(10, false, true) < 0f)
        {
            SurvivalPlayerScript.instance.Die("Killed by another player!");
        }

        takingDamage = false;
    }

    public void Update()
    {
        if (mCam == null)
        {
            Camera[] allCams = Camera.allCameras;
            foreach (Camera c in allCams)
            {
                if (c != null)
                {
                    if (c.gameObject.name.Equals("Camera"))
                    {
                        mCam = c;
                    }
                }
            }
        }
        else
        {
            if (gun == null)
            {
                CreateGun();
            }
            else
            {
                ManageGun();
            }
        }

        if (GameState.PlayerSpawned)
        {
            UpdateGame();
        }
    }

    private void UpdateGame()
    {
        if (Input.GetKeyDown(KeyCode.Comma))
        {
            if (usingGun == false)
            {
                WeaponSelectScript.meNextWeapon = eWeaponType.eNone;
                usingGun = true;
            }
            else
            {
                WeaponSelectScript.meNextWeapon = eWeaponType.eLaserDrill;
                usingGun = false;
            }
        }

        if (usingGun == true)
        {
            if (Input.GetKey(KeyCode.Mouse0))
            {
                FireGun();
            }

            if (ModManager.mModConfigurations.ModsById.ContainsKey("Maverick.CombatScanner"))
            {
                if (Input.GetKeyDown(KeyCode.Period))
                {
                    usingGun = false;
                }
            }
        }

        if (takingDamage == true)
        {
            TakeDamage();
        }
    }
}
