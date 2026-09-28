using System.Collections.Generic;
using UnityEngine;

namespace AIGame2D.Player
{
    /// <summary>
    /// Estados possíveis do Soldado na Máquina de Estados Finita (FSM).
    /// </summary>
    public enum SoldierState
    {
        Idle,
        Running,
        Shooting,
        Punching,
        Dead
    }

    /// <summary>
    /// Controlador do jogador (Soldado) em visão lateral 2D.
    /// Gerencia a máquina de estados, movimentação, ações e sincronização
    /// dos parâmetros com o Animator:
    /// - Speed (float)
    /// - Shoot (trigger)
    /// - Punch (trigger)
    /// - Die (trigger)
    /// - IsDead (bool)
    /// </summary>
    [RequireComponent(typeof(Animator))]
    [RequireComponent(typeof(SpriteRenderer))]
    public class SoldierController : MonoBehaviour
    {
        [Header("--- Configurações de Movimento ---")]
        [Tooltip("Velocidade horizontal de movimento")]
        [SerializeField] private float moveSpeed = 5.0f;

        [Tooltip("Orientação inicial olhando para a direita")]
        [SerializeField] private bool facingRight = true;

        [Header("--- Configurações de Combate ---")]
        [Tooltip("Duração do estado de tiro (em segundos) antes de retornar ao estado anterior")]
        [SerializeField] private float shootDuration = 0.33f;

        [Tooltip("Duração do estado de soco (em segundos) antes de retornar ao estado anterior")]
        [SerializeField] private float punchDuration = 0.33f;

        [Tooltip("Se verdadeiro, o soldado não pode andar enquanto atira ou soca")]
        [SerializeField] private bool lockMovementDuringAttacks = true;

        [Header("--- Controles de Entrada (Teclado) ---")]
        [Tooltip("Tecla para Atirar")]
        [SerializeField] private KeyCode shootKey = KeyCode.J;

        [Tooltip("Tecla secundária para Atirar")]
        [SerializeField] private KeyCode shootKeyAlt = KeyCode.X;

        [Tooltip("Tecla para Socar")]
        [SerializeField] private KeyCode punchKey = KeyCode.K;

        [Tooltip("Tecla secundária para Socar")]
        [SerializeField] private KeyCode punchKeyAlt = KeyCode.Z;

        [Tooltip("Tecla de teste para simular morte")]
        [SerializeField] private KeyCode testDieKey = KeyCode.Alpha0;

        [Tooltip("Tecla de teste para renascer após a morte")]
        [SerializeField] private KeyCode testRespawnKey = KeyCode.R;

        // Referências de componentes
        private Animator animator;
        private SpriteRenderer spriteRenderer;
        private Rigidbody2D rb2d;

        // Máquina de estados
        private SoldierState currentState = SoldierState.Idle;
        private float stateTimer = 0f;
        private float currentHorizontalInput = 0f;

        // Hashes dos parâmetros do Animator (Otimização sem Garbage Collection)
        private static readonly int AnimSpeed = Animator.StringToHash("Speed");
        private static readonly int AnimShoot = Animator.StringToHash("Shoot");
        private static readonly int AnimPunch = Animator.StringToHash("Punch");
        private static readonly int AnimDie = Animator.StringToHash("Die");
        private static readonly int AnimIsDead = Animator.StringToHash("IsDead");

        // Hashes de fallback em português (compatibilidade com Soldier_Controller existente)
        private static readonly int AnimAtirarFallback = Animator.StringToHash("Atirar");
        private static readonly int AnimSocarFallback = Animator.StringToHash("Socar");
        private static readonly int AnimMorrerFallback = Animator.StringToHash("Morrer");

        // Cache de parâmetros existentes no Animator anexado
        private readonly HashSet<int> availableParameters = new HashSet<int>();

        #region Propriedades Públicas

        /// <summary>
        /// Estado atual da Máquina de Estados.
        /// </summary>
        public SoldierState CurrentState => currentState;

        /// <summary>
        /// Indica se o soldado está morto.
        /// </summary>
        public bool IsDead => currentState == SoldierState.Dead;

        /// <summary>
        /// Velocidade de deslocamento configurada.
        /// </summary>
        public float MoveSpeed
        {
            get => moveSpeed;
            set => moveSpeed = Mathf.Max(0f, value);
        }

        #endregion

        #region Ciclo de Vida Unity

        private void Awake()
        {
            animator = GetComponent<Animator>();
            spriteRenderer = GetComponent<SpriteRenderer>();
            rb2d = GetComponent<Rigidbody2D>();

            CacheAnimatorParameters();
        }

        private void Start()
        {
            ChangeState(SoldierState.Idle);
        }

        private void Update()
        {
            HandleInput();
            UpdateState();
            UpdateAnimator();
        }

        private void FixedUpdate()
        {
            ApplyMovement();
        }

        #endregion

        #region Tratamento de Entradas (Input)

        private void HandleInput()
        {
            // Se estiver morto, apenas permite testar o renascimento
            if (currentState == SoldierState.Dead)
            {
                if (Input.GetKeyDown(testRespawnKey))
                {
                    Respawn();
                }
                return;
            }

            // Tecla de teste rápido de morte
            if (Input.GetKeyDown(testDieKey))
            {
                Die();
                return;
            }

            // Leitura do movimento horizontal (A/D ou Setas)
            currentHorizontalInput = Input.GetAxisRaw("Horizontal");

            // Não aceita novas ações ofensivas se já estiver atacando
            if (currentState == SoldierState.Shooting || currentState == SoldierState.Punching)
            {
                return;
            }

            // Ação: Atirar (J, X ou Botão esquerdo do mouse)
            if (Input.GetKeyDown(shootKey) || Input.GetKeyDown(shootKeyAlt) || Input.GetButtonDown("Fire1"))
            {
                Shoot();
                return;
            }

            // Ação: Socar (K, Z ou Botão direito do mouse)
            if (Input.GetKeyDown(punchKey) || Input.GetKeyDown(punchKeyAlt) || Input.GetButtonDown("Fire2"))
            {
                Punch();
                return;
            }
        }

        #endregion

        #region Máquina de Estados (State Machine)

        private void UpdateState()
        {
            switch (currentState)
            {
                case SoldierState.Idle:
                    if (Mathf.Abs(currentHorizontalInput) > 0.05f)
                    {
                        ChangeState(SoldierState.Running);
                    }
                    break;

                case SoldierState.Running:
                    if (Mathf.Abs(currentHorizontalInput) <= 0.05f)
                    {
                        ChangeState(SoldierState.Idle);
                    }
                    else
                    {
                        UpdateFacingDirection(currentHorizontalInput);
                    }
                    break;

                case SoldierState.Shooting:
                    stateTimer -= Time.deltaTime;
                    if (stateTimer <= 0f)
                    {
                        // Retorna para Running se ainda houver input, senão Idle
                        ChangeState(Mathf.Abs(currentHorizontalInput) > 0.05f ? SoldierState.Running : SoldierState.Idle);
                    }
                    break;

                case SoldierState.Punching:
                    stateTimer -= Time.deltaTime;
                    if (stateTimer <= 0f)
                    {
                        ChangeState(Mathf.Abs(currentHorizontalInput) > 0.05f ? SoldierState.Running : SoldierState.Idle);
                    }
                    break;

                case SoldierState.Dead:
                    // Fica inerte até Respawn()
                    break;
            }
        }

        /// <summary>
        /// Transiciona para um novo estado, executando lógicas de entrada de estado.
        /// </summary>
        public void ChangeState(SoldierState newState)
        {
            if (currentState == SoldierState.Dead && newState != SoldierState.Idle)
            {
                // Não pode sair da morte a não ser via Respawn()
                return;
            }

            currentState = newState;

            switch (newState)
            {
                case SoldierState.Idle:
                    stateTimer = 0f;
                    break;

                case SoldierState.Running:
                    stateTimer = 0f;
                    break;

                case SoldierState.Shooting:
                    stateTimer = shootDuration;
                    TriggerAnimation(AnimShoot, AnimAtirarFallback);
                    break;

                case SoldierState.Punching:
                    stateTimer = punchDuration;
                    TriggerAnimation(AnimPunch, AnimSocarFallback);
                    break;

                case SoldierState.Dead:
                    stateTimer = 0f;
                    currentHorizontalInput = 0f;
                    TriggerAnimation(AnimDie, AnimMorrerFallback);
                    SetBoolAnimation(AnimIsDead, true);
                    break;
            }
        }

        #endregion

        #region Movimentação e Física

        private void ApplyMovement()
        {
            if (currentState == SoldierState.Dead)
            {
                if (rb2d != null)
                {
                    rb2d.linearVelocity = new Vector2(0f, rb2d.linearVelocity.y);
                }
                return;
            }

            if (lockMovementDuringAttacks && (currentState == SoldierState.Shooting || currentState == SoldierState.Punching))
            {
                if (rb2d != null)
                {
                    rb2d.linearVelocity = new Vector2(0f, rb2d.linearVelocity.y);
                }
                return;
            }

            float targetVelocityX = currentHorizontalInput * moveSpeed;

            if (rb2d != null)
            {
                // Se Rigidbody2D estiver presente, move por física
                rb2d.linearVelocity = new Vector2(targetVelocityX, rb2d.linearVelocity.y);
            }
            else
            {
                // Fallback para movimentação por Transform direto
                transform.Translate(Vector3.right * (targetVelocityX * Time.fixedDeltaTime));
            }
        }

        private void UpdateFacingDirection(float horizontal)
        {
            if (horizontal > 0.01f && !facingRight)
            {
                Flip(true);
            }
            else if (horizontal < -0.01f && facingRight)
            {
                Flip(false);
            }
        }

        private void Flip(bool right)
        {
            facingRight = right;
            spriteRenderer.flipX = !right;
        }

        #endregion

        #region Integração com o Animator

        private void UpdateAnimator()
        {
            if (animator == null) return;

            // Atualiza parâmetro Speed (magnitude da velocidade para transição Idle <-> Correr)
            float speedValue = (currentState == SoldierState.Dead || 
                               (lockMovementDuringAttacks && (currentState == SoldierState.Shooting || currentState == SoldierState.Punching)))
                               ? 0f
                               : Mathf.Abs(currentHorizontalInput);

            SetFloatAnimation(AnimSpeed, speedValue);
        }

        private void TriggerAnimation(int primaryHash, int fallbackHash)
        {
            if (animator == null) return;

            if (availableParameters.Contains(primaryHash))
            {
                animator.SetTrigger(primaryHash);
            }
            else if (availableParameters.Contains(fallbackHash))
            {
                animator.SetTrigger(fallbackHash);
            }
        }

        private void SetFloatAnimation(int paramHash, float value)
        {
            if (animator != null && availableParameters.Contains(paramHash))
            {
                animator.SetFloat(paramHash, value);
            }
        }

        private void SetBoolAnimation(int paramHash, bool value)
        {
            if (animator != null && availableParameters.Contains(paramHash))
            {
                animator.SetBool(paramHash, value);
            }
        }

        private void CacheAnimatorParameters()
        {
            availableParameters.Clear();
            if (animator == null) return;

            foreach (var param in animator.parameters)
            {
                availableParameters.Add(param.nameHash);
            }
        }

        #endregion

        #region Comandos Públicos (Ações do Soldado)

        /// <summary>
        /// Dispara o comando de Atirar (animação Soldier_Atirar).
        /// </summary>
        public void Shoot()
        {
            if (currentState != SoldierState.Dead && currentState != SoldierState.Shooting && currentState != SoldierState.Punching)
            {
                ChangeState(SoldierState.Shooting);
            }
        }

        /// <summary>
        /// Dispara o comando de Socar (animação Soldier_Socar).
        /// </summary>
        public void Punch()
        {
            if (currentState != SoldierState.Dead && currentState != SoldierState.Shooting && currentState != SoldierState.Punching)
            {
                ChangeState(SoldierState.Punching);
            }
        }

        /// <summary>
        /// Mata o soldado (animação Soldier_Morrer) e bloqueia ações.
        /// </summary>
        public void Die()
        {
            if (currentState != SoldierState.Dead)
            {
                ChangeState(SoldierState.Dead);
            }
        }

        /// <summary>
        /// Renasce o soldado, restaurando o estado para Idle.
        /// </summary>
        public void Respawn()
        {
            SetBoolAnimation(AnimIsDead, false);
            ChangeState(SoldierState.Idle);
            if (animator != null)
            {
                animator.Play("Idle", 0, 0f);
            }
        }

        /// <summary>
        /// Define a velocidade horizontal externamente (ex: IA ou Joystick virtual).
        /// </summary>
        public void SetMoveInput(float horizontal)
        {
            currentHorizontalInput = Mathf.Clamp(horizontal, -1f, 1f);
        }

        #endregion
    }
}
