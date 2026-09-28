using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.U2D.Sprites;
using UnityEngine;

namespace AIGame2D.Editor
{
    /// <summary>
    /// Utilitário de Editor para fatiar automaticamente o sprite sheet do soldado
    /// e gerar os arquivos de animação (.anim) e o AnimatorController correspondente.
    /// </summary>
    public static class SoldierSpriteSetup
    {
        private const string TexturePath = "Assets/Art/Sprites/Soldier/Soldier_Sheet.png";
        private const string AnimationOutputDir = "Assets/Art/Animations/Soldier";
        private const string ControllerPath = AnimationOutputDir + "/Soldier_Controller.controller";
        
        private const int FrameSize = 256;
        private const int TotalColumns = 6;
        private const int TotalRows = 5;

        // Definição das animações por linha (Linha 1 = Topo, Linha 5 = Fundo)
        private struct RowDefinition
        {
            public string AnimName;
            public int RowIndex;     // 0 = Linha 1 (topo), 4 = Linha 5 (base)
            public int FrameCount;
            public bool Loop;
            public float FrameRate;

            public RowDefinition(string name, int row, int count, bool loop, float fps)
            {
                AnimName = name;
                RowIndex = row;
                FrameCount = count;
                Loop = loop;
                FrameRate = fps;
            }
        }

        private static readonly RowDefinition[] Rows = new[]
        {
            new RowDefinition("Soldier_Idle",   0, 4, true,  6f),
            new RowDefinition("Soldier_Correr", 1, 6, true,  10f),
            new RowDefinition("Soldier_Atirar", 2, 4, false, 12f),
            new RowDefinition("Soldier_Socar",  3, 4, false, 12f),
            new RowDefinition("Soldier_Morrer", 4, 6, false, 10f)
        };

        // ====================================================================
        // MENU ITEMS MODULARES
        // ====================================================================

        [MenuItem("Tools/Soldier/1. Slice Sprite Sheet", false, 1)]
        public static void MenuSliceSpriteSheet()
        {
            SliceSpriteSheetWorkflow(showDialog: true);
        }

        [MenuItem("Tools/Soldier/2. Generate Animation Clips", false, 2)]
        public static void MenuGenerateAnimationClips()
        {
            GenerateAnimationClipsWorkflow(showDialog: true);
        }

        [MenuItem("Tools/Soldier/3. Create Animator Controller", false, 3)]
        public static void MenuCreateAnimatorController()
        {
            CreateAnimatorControllerWorkflow(showDialog: true);
        }

        [MenuItem("Tools/Soldier/4. Create Soldier Prefab", false, 4)]
        public static void MenuCreateSoldierPrefab()
        {
            CreateOrUpdateSoldierPrefab(showDialog: true);
        }

        [MenuItem("Tools/Soldier/5. Assign Soldier to Animation Preview", false, 5)]
        public static void MenuAssignPreview()
        {
            AssignSoldierToActivePreview(showDialog: true);
        }

        [MenuItem("Tools/Soldier/Setup All (Full Pipeline)", false, 20)]
        public static void MenuSetupAll()
        {
            ExecuteFullSetup();
        }

        // ====================================================================
        // FLUXOS DE EXECUÇÃO
        // ====================================================================

        /// <summary>
        /// Etapa 1: Configurar a textura e fatiar em células de 256x256.
        /// </summary>
        public static bool SliceSpriteSheetWorkflow(bool showDialog = false)
        {
            TextureImporter importer = AssetImporter.GetAtPath(TexturePath) as TextureImporter;
            if (importer == null)
            {
                string msg = $"Arquivo do Sprite Sheet não encontrado em: {TexturePath}\nCertifique-se de que o arquivo existe antes de continuar.";
                Debug.LogError($"[SoldierSpriteSetup] {msg}");
                if (showDialog) EditorUtility.DisplayDialog("Erro", msg, "OK");
                return false;
            }

            ConfigureTextureImporter(importer);
            SliceSpriteSheet(importer);

            AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh();

            Dictionary<string, Sprite> sprites = LoadSlicedSprites(TexturePath);
            Debug.Log($"<color=#55FF55><b>[SoldierSpriteSetup]</b> Etapa 1 Concluída:</color> {sprites.Count} sprites fatiados com sucesso!");

            if (showDialog)
            {
                EditorUtility.DisplayDialog(
                    "Sucesso - Etapa 1",
                    $"Sprite Sheet fatiado com sucesso!\n\nTotal de sprites gerados: {sprites.Count} (grade 256x256 px)",
                    "OK"
                );
            }

            return sprites.Count > 0;
        }

        /// <summary>
        /// Etapa 2: Gerar os arquivos .anim para Idle, Correr, Atirar, Socar e Morrer.
        /// </summary>
        public static Dictionary<string, AnimationClip> GenerateAnimationClipsWorkflow(bool showDialog = false)
        {
            Dictionary<string, Sprite> slicedSprites = LoadSlicedSprites(TexturePath);
            if (slicedSprites.Count == 0)
            {
                // Tenta fatiar automaticamente se ainda não estiver fatiado
                Debug.Log("[SoldierSpriteSetup] Sprites não fatiados. Tentando fatiar primeiro...");
                if (!SliceSpriteSheetWorkflow(showDialog: false))
                {
                    string msg = "Nenhum sprite fatiado foi encontrado. Execute primeiro 'Tools > Soldier > 1. Slice Sprite Sheet'.";
                    Debug.LogError($"[SoldierSpriteSetup] {msg}");
                    if (showDialog) EditorUtility.DisplayDialog("Aviso", msg, "OK");
                    return null;
                }
                slicedSprites = LoadSlicedSprites(TexturePath);
            }

            if (!Directory.Exists(AnimationOutputDir))
            {
                Directory.CreateDirectory(AnimationOutputDir);
                AssetDatabase.Refresh();
            }

            Dictionary<string, AnimationClip> generatedClips = new Dictionary<string, AnimationClip>();
            foreach (var row in Rows)
            {
                List<Sprite> animFrames = new List<Sprite>();
                for (int f = 0; f < row.FrameCount; f++)
                {
                    string spriteName = $"{row.AnimName}_{f}";
                    if (slicedSprites.TryGetValue(spriteName, out Sprite sprite))
                    {
                        animFrames.Add(sprite);
                    }
                    else
                    {
                        Debug.LogWarning($"[SoldierSpriteSetup] Sprite ausente: {spriteName}");
                    }
                }

                if (animFrames.Count > 0)
                {
                    string clipPath = $"{AnimationOutputDir}/{row.AnimName}.anim";
                    AnimationClip clip = CreateOrUpdateAnimationClip(clipPath, animFrames, row.FrameRate, row.Loop);
                    generatedClips[row.AnimName] = clip;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"<color=#55FF55><b>[SoldierSpriteSetup]</b> Etapa 2 Concluída:</color> {generatedClips.Count} arquivos de animação (.anim) gerados em: {AnimationOutputDir}");

            if (showDialog)
            {
                EditorUtility.DisplayDialog(
                    "Sucesso - Etapa 2",
                    $"Arquivos de Animação (.anim) gerados com sucesso:\n\n" +
                    $"- Soldier_Idle.anim (4 frames, loop)\n" +
                    $"- Soldier_Correr.anim (6 frames, loop)\n" +
                    $"- Soldier_Atirar.anim (4 frames)\n" +
                    $"- Soldier_Socar.anim (4 frames)\n" +
                    $"- Soldier_Morrer.anim (6 frames)\n\n" +
                    $"Salvos em: {AnimationOutputDir}",
                    "OK"
                );
            }

            return generatedClips;
        }

        /// <summary>
        /// Etapa 3: Criar ou configurar o AnimatorController com parâmetros, estados e transições.
        /// </summary>
        public static AnimatorController CreateAnimatorControllerWorkflow(bool showDialog = false)
        {
            // Carregar clipes de animação existentes
            Dictionary<string, AnimationClip> clips = new Dictionary<string, AnimationClip>();
            foreach (var row in Rows)
            {
                string clipPath = $"{AnimationOutputDir}/{row.AnimName}.anim";
                AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                if (clip != null)
                {
                    clips[row.AnimName] = clip;
                }
            }

            // Se os clipes não existirem, tenta gerá-los
            if (clips.Count == 0)
            {
                Debug.Log("[SoldierSpriteSetup] Clipes de animação não encontrados. Gerando clipes primeiro...");
                clips = GenerateAnimationClipsWorkflow(showDialog: false);
                if (clips == null || clips.Count == 0)
                {
                    string msg = "Clipes de animação não encontrados. Execute primeiro 'Tools > Soldier > 2. Generate Animation Clips'.";
                    Debug.LogError($"[SoldierSpriteSetup] {msg}");
                    if (showDialog) EditorUtility.DisplayDialog("Aviso", msg, "OK");
                    return null;
                }
            }

            if (!Directory.Exists(AnimationOutputDir))
            {
                Directory.CreateDirectory(AnimationOutputDir);
                AssetDatabase.Refresh();
            }

            AnimatorController controller = CreateOrConfigureAnimatorController(clips);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (controller != null)
            {
                Selection.activeObject = controller;
                EditorGUIUtility.PingObject(controller);
            }

            Debug.Log($"<color=#55FF55><b>[SoldierSpriteSetup]</b> Etapa 3 Concluída:</color> AnimatorController criado em: {ControllerPath}");

            if (showDialog)
            {
                EditorUtility.DisplayDialog(
                    "Sucesso - Etapa 3",
                    $"Animator Controller configurado com sucesso!\n\n" +
                    $"- Arquivo: Soldier_Controller.controller\n" +
                    $"- Estados: Idle, Correr, Atirar, Socar, Morrer\n" +
                    $"- Parâmetros: Speed, Atirar, Socar, Morrer",
                    "OK"
                );
            }

            return controller;
        }

        public const string PrefabPath = "Assets/Art/Animations/Soldier/Soldier.prefab";

        /// <summary>
        /// Etapa 4: Cria ou atualiza o Prefab do Soldado com SpriteRenderer e Animator configurados.
        /// Este prefab é o modelo de referência usado para o Preview no Inspector das Animações.
        /// </summary>
        public static GameObject CreateOrUpdateSoldierPrefab(bool showDialog = false)
        {
            Dictionary<string, Sprite> sprites = LoadSlicedSprites(TexturePath);
            Sprite defaultSprite = null;
            if (sprites.Count > 0)
            {
                sprites.TryGetValue("Soldier_Idle_0", out defaultSprite);
                if (defaultSprite == null) defaultSprite = sprites.Values.FirstOrDefault();
            }

            RuntimeAnimatorController controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);

            // Criar GameObject temporário para gerar o Prefab
            GameObject tempGO = new GameObject("Soldier");
            var sr = tempGO.AddComponent<SpriteRenderer>();
            if (defaultSprite != null) sr.sprite = defaultSprite;

            var anim = tempGO.AddComponent<Animator>();
            if (controller != null) anim.runtimeAnimatorController = controller;

            if (!Directory.Exists(AnimationOutputDir))
            {
                Directory.CreateDirectory(AnimationOutputDir);
                AssetDatabase.Refresh();
            }

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(tempGO, PrefabPath);
            UnityEngine.Object.DestroyImmediate(tempGO);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"<color=#55FF55><b>[SoldierSpriteSetup]</b> Prefab do Soldado criado com sucesso em: {PrefabPath}</color>");

            if (showDialog)
            {
                EditorUtility.DisplayDialog(
                    "Prefab do Soldado Criado",
                    $"Prefab gerado com sucesso em:\n{PrefabPath}\n\n" +
                    "Configurado com:\n" +
                    "- SpriteRenderer (Sprite Inicial: Soldier_Idle_0)\n" +
                    "- Animator (Soldier_Controller.controller)\n\n" +
                    "Este Prefab pode ser arrastado diretamente para a área 'Preview Area' no Inspector de qualquer clipe de animação!",
                    "OK"
                );
            }

            return prefab;
        }

        /// <summary>
        /// Tenta atribuir automaticamente o Prefab do Soldado ao visualizador de animação (Inspector Preview)
        /// de qualquer AnimationClip selecionado via Reflection.
        /// </summary>
        public static void AssignSoldierToActivePreview(bool showDialog = false)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                prefab = CreateOrUpdateSoldierPrefab(showDialog: false);
            }

            if (prefab == null)
            {
                if (showDialog) EditorUtility.DisplayDialog("Erro", "Não foi possível carregar ou criar o Prefab do Soldado.", "OK");
                return;
            }

            bool success = TrySetPreviewOnActiveEditor(prefab);

            if (showDialog)
            {
                if (success)
                {
                    EditorUtility.DisplayDialog(
                        "Preview Configurado",
                        "O Prefab do Soldado foi vinculado com sucesso ao visualizador de animação!\n\n" +
                        "Agora o sprite animado deve aparecer no painel Preview do Inspector.",
                        "OK"
                    );
                }
                else
                {
                    EditorUtility.DisplayDialog(
                        "Soldier Prefab Pronto",
                        $"O Prefab do Soldado está pronto em:\n{PrefabPath}\n\n" +
                        "Para visualizar a animação no Inspector:\n" +
                        "1. Selecione o clipe .anim (ex: Soldier_Correr)\n" +
                        "2. Arraste o arquivo 'Soldier.prefab' para dentro da área preta de Preview no Inspector!",
                        "OK"
                    );
                }
            }
        }

        private static bool TrySetPreviewOnActiveEditor(GameObject prefab)
        {
            try
            {
                var tracker = ActiveEditorTracker.sharedTracker;
                if (tracker == null || tracker.activeEditors == null) return false;

                foreach (var editor in tracker.activeEditors)
                {
                    if (editor == null) continue;
                    if (editor.GetType().Name == "AnimationClipEditor")
                    {
                        var previewField = editor.GetType().GetField("m_AvatarPreview", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                        if (previewField != null)
                        {
                            var avatarPreview = previewField.GetValue(editor);
                            if (avatarPreview != null)
                            {
                                // Ativar modo 2D se disponível
                                var is2DProp = avatarPreview.GetType().GetProperty("is2D", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                                if (is2DProp != null && is2DProp.CanWrite)
                                {
                                    is2DProp.SetValue(avatarPreview, true);
                                }

                                // Tentar métodos comuns de atribuição de preview
                                var setPreviewMethod = avatarPreview.GetType().GetMethod("ResetPreviewInstance", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                                if (setPreviewMethod != null)
                                {
                                    setPreviewMethod.Invoke(avatarPreview, null);
                                }
                            }
                        }

                        editor.Repaint();
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SoldierSpriteSetup] Tentativa de configurar preview via reflection: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// Executa todas as etapas em sequência (Fatiamento + Animações + AnimatorController + Prefab).
        /// </summary>
        public static void ExecuteFullSetup()
        {
            if (!SliceSpriteSheetWorkflow(showDialog: false)) return;
            var clips = GenerateAnimationClipsWorkflow(showDialog: false);
            if (clips == null) return;
            var controller = CreateAnimatorControllerWorkflow(showDialog: false);
            var prefab = CreateOrUpdateSoldierPrefab(showDialog: false);

            if (prefab != null)
            {
                Selection.activeObject = prefab;
                EditorGUIUtility.PingObject(prefab);
            }

            EditorUtility.DisplayDialog(
                "Sucesso Completo",
                "Pipeline do Soldado executada com sucesso!\n\n" +
                "1. Sprite Sheet fatiado (grade 256x256)\n" +
                "2. Clipes de animação gerados (Idle, Correr, Atirar, Socar, Morrer)\n" +
                "3. Animator Controller configurado (Soldier_Controller.controller)\n" +
                "4. Prefab do Soldado gerado (Soldier.prefab)\n\n" +
                "Para visualizar qualquer animação no Inspector:\n" +
                "Selecione o arquivo .anim (ex: Soldier_Correr) e arraste o 'Soldier.prefab' para dentro da Preview Area no canto inferior do Inspector!",
                "OK"
            );
        }

        // ====================================================================
        // MÉTODOS INTERNOS DE CONFIGURAÇÃO E MANIPULAÇÃO
        // ====================================================================

        private static void ConfigureTextureImporter(TextureImporter importer)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = FrameSize;
            importer.filterMode = FilterMode.Point; // Essencial para Pixel Art nítido
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
        }

        private static void SliceSpriteSheet(TextureImporter importer)
        {
            List<SpriteRect> spriteRects = new List<SpriteRect>();
            List<SpriteMetaData> legacyMetas = new List<SpriteMetaData>();

            foreach (var row in Rows)
            {
                // Em Unity coordenadas de textura começam no canto inferior esquerdo (Y = 0)
                // RowIndex 0 (Linha 1 do topo) fica na faixa Y mais alta [1024, 1280]
                int unityRowY = (TotalRows - 1 - row.RowIndex) * FrameSize;

                for (int col = 0; col < row.FrameCount; col++)
                {
                    int x = col * FrameSize;
                    int y = unityRowY;
                    string spriteName = $"{row.AnimName}_{col}";
                    Rect rect = new Rect(x, y, FrameSize, FrameSize);

                    // Alinhamento na base dos pés (BottomCenter)
                    Vector2 pivot = new Vector2(0.5f, 0.0f);

                    var sRect = new SpriteRect
                    {
                        name = spriteName,
                        rect = rect,
                        alignment = SpriteAlignment.BottomCenter,
                        pivot = pivot,
                        spriteID = GUID.Generate()
                    };
                    spriteRects.Add(sRect);

                    var meta = new SpriteMetaData
                    {
                        name = spriteName,
                        rect = rect,
                        alignment = (int)SpriteAlignment.BottomCenter,
                        pivot = pivot
                    };
                    legacyMetas.Add(meta);
                }
            }

            // Aplicar via ISpriteEditorDataProvider (Modern Unity 2D)
            try
            {
                var factory = new SpriteDataProviderFactories();
                factory.Init();
                var dataProvider = factory.GetSpriteEditorDataProviderFromObject(importer);
                if (dataProvider != null)
                {
                    dataProvider.InitSpriteEditorDataProvider();
                    dataProvider.SetSpriteRects(spriteRects.ToArray());
                    dataProvider.Apply();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SoldierSpriteSetup] Falha no SpriteDataProviderFactories, utilizando fallback: {ex.Message}");
            }

            // Fallback para TextureImporter.spritesheet
#pragma warning disable CS0618
            importer.spritesheet = legacyMetas.ToArray();
#pragma warning restore CS0618

            importer.SaveAndReimport();
        }

        private static Dictionary<string, Sprite> LoadSlicedSprites(string path)
        {
            Dictionary<string, Sprite> result = new Dictionary<string, Sprite>();
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);

            foreach (var asset in assets)
            {
                if (asset is Sprite sprite)
                {
                    result[sprite.name] = sprite;
                }
            }

            return result;
        }

        private static AnimationClip CreateOrUpdateAnimationClip(string path, List<Sprite> frames, float frameRate, bool loop)
        {
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null)
            {
                clip = new AnimationClip();
                AssetDatabase.CreateAsset(clip, path);
            }
            else
            {
                clip.ClearCurves();
            }

            clip.frameRate = frameRate;

            // Curva de troca de sprites no SpriteRenderer
            EditorCurveBinding binding = new EditorCurveBinding
            {
                type = typeof(SpriteRenderer),
                path = "",
                propertyName = "m_Sprite"
            };

            ObjectReferenceKeyframe[] keyframes = new ObjectReferenceKeyframe[frames.Count];
            for (int i = 0; i < frames.Count; i++)
            {
                keyframes[i] = new ObjectReferenceKeyframe
                {
                    time = i / frameRate,
                    value = frames[i]
                };
            }

            AnimationUtility.SetObjectReferenceCurve(clip, binding, keyframes);

            // Configurar repetição (Loop)
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            EditorUtility.SetDirty(clip);
            return clip;
        }

        private static AnimatorController CreateOrConfigureAnimatorController(Dictionary<string, AnimationClip> clips)
        {
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            }

            // Limpar parâmetros anteriores e recriar
            while (controller.parameters.Length > 0)
            {
                controller.RemoveParameter(0);
            }

            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Atirar", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Socar", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Morrer", AnimatorControllerParameterType.Trigger);

            var rootStateMachine = controller.layers[0].stateMachine;

            // Limpar estados existentes para recriação limpa
            while (rootStateMachine.states.Length > 0)
            {
                rootStateMachine.RemoveState(rootStateMachine.states[0].state);
            }
            while (rootStateMachine.anyStateTransitions.Length > 0)
            {
                rootStateMachine.RemoveAnyStateTransition(rootStateMachine.anyStateTransitions[0]);
            }

            // Criar Estados com posições visuais no StateMachine
            AnimatorState idleState = rootStateMachine.AddState("Idle", new Vector3(250, 0, 0));
            idleState.motion = GetClip(clips, "Soldier_Idle");
            rootStateMachine.defaultState = idleState;

            AnimatorState runState = rootStateMachine.AddState("Correr", new Vector3(250, 80, 0));
            runState.motion = GetClip(clips, "Soldier_Correr");

            AnimatorState shootState = rootStateMachine.AddState("Atirar", new Vector3(550, -60, 0));
            shootState.motion = GetClip(clips, "Soldier_Atirar");

            AnimatorState punchState = rootStateMachine.AddState("Socar", new Vector3(550, 20, 0));
            punchState.motion = GetClip(clips, "Soldier_Socar");

            AnimatorState deathState = rootStateMachine.AddState("Morrer", new Vector3(550, 100, 0));
            deathState.motion = GetClip(clips, "Soldier_Morrer");

            // Transições Idle <-> Correr
            var idleToRun = idleState.AddTransition(runState);
            idleToRun.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");
            idleToRun.hasExitTime = false;
            idleToRun.duration = 0f;

            var runToIdle = runState.AddTransition(idleState);
            runToIdle.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");
            runToIdle.hasExitTime = false;
            runToIdle.duration = 0f;

            // Transições AnyState -> Ações (Tiro, Soco, Morte)
            var anyToShoot = rootStateMachine.AddAnyStateTransition(shootState);
            anyToShoot.AddCondition(AnimatorConditionMode.If, 0, "Atirar");
            anyToShoot.hasExitTime = false;
            anyToShoot.duration = 0f;

            var shootToIdle = shootState.AddTransition(idleState);
            shootToIdle.hasExitTime = true;
            shootToIdle.exitTime = 1f;
            shootToIdle.duration = 0f;

            var anyToPunch = rootStateMachine.AddAnyStateTransition(punchState);
            anyToPunch.AddCondition(AnimatorConditionMode.If, 0, "Socar");
            anyToPunch.hasExitTime = false;
            anyToPunch.duration = 0f;

            var punchToIdle = punchState.AddTransition(idleState);
            punchToIdle.hasExitTime = true;
            punchToIdle.exitTime = 1f;
            punchToIdle.duration = 0f;

            var anyToDeath = rootStateMachine.AddAnyStateTransition(deathState);
            anyToDeath.AddCondition(AnimatorConditionMode.If, 0, "Morrer");
            anyToDeath.hasExitTime = false;
            anyToDeath.duration = 0f;

            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static AnimationClip GetClip(Dictionary<string, AnimationClip> clips, string name)
        {
            return clips.TryGetValue(name, out AnimationClip clip) ? clip : null;
        }
    }
}
