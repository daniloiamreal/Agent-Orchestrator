const API_BASE_URL = 'http://localhost:5130';

// Elementos do DOM
const promptInput = document.getElementById('promptInput');
const executeBtn = document.getElementById('executeBtn');
const executeAutonomousBtn = document.getElementById('executeAutonomousBtn');
const logsContainer = document.getElementById('logsContainer');
const clearBtn = document.getElementById('clearBtn');
const cancelBtn = document.getElementById('cancelBtn');
const statusIndicator = document.getElementById('statusIndicator');
const signalrStatus = document.getElementById('signalrStatus');
const planSection = document.getElementById('planSection');
const planSteps = document.getElementById('planSteps');
const planStatus = document.getElementById('planStatus');
const progressBar = document.getElementById('progressBar');
const responseSection = document.getElementById('responseSection');
const responseContent = document.getElementById('responseContent');
const copyResponseBtn = document.getElementById('copyResponseBtn');
const agentsPanel = document.getElementById('agentsPanel');

let eventSource = null;
let hubConnection = null;
let currentTaskId = null;
let currentResponse = '';

// ============================================
// AGENT CARD MANAGEMENT
// ============================================

// Conjunto para rastrear quais agentes foram usados nesta execução
let usedAgents = new Set();

function resetAllAgents() {
    usedAgents.clear();
    document.querySelectorAll('.agent-card').forEach(card => {
        card.removeAttribute('data-status');
        card.querySelector('.agent-status').textContent = 'Aguardando';
    });
}

function setAgentStatus(agentName, status, statusText) {
    // Normalizar nome do agente
    const normalizedName = agentName.replace('Agent', '');
    
    document.querySelectorAll('.agent-card').forEach(card => {
        const cardAgent = card.dataset.agent;
        if (cardAgent && cardAgent.toLowerCase().includes(normalizedName.toLowerCase())) {
            card.setAttribute('data-status', status);
            card.querySelector('.agent-status').textContent = statusText;
            
            // Rastrear agentes usados
            if (status === 'working' || status === 'completed') {
                usedAgents.add(cardAgent);
            }
        }
    });
}

function setAgentWorking(agentName) {
    setAgentStatus(agentName, 'working', '🔄 Trabalhando...');
}

function setAgentCompleted(agentName) {
    setAgentStatus(agentName, 'completed', '✅ Concluído');
}

function setAgentError(agentName) {
    setAgentStatus(agentName, 'error', '❌ Erro');
}

// ============================================
// SIGNALR CONNECTION
// ============================================
async function initSignalR() {
    hubConnection = new signalR.HubConnectionBuilder()
        .withUrl(`${API_BASE_URL}/hubs/agent`)
        .withAutomaticReconnect()
        .build();

    // Event handlers
    hubConnection.on("OnPlanCreated", (event) => {
        console.log("Plan created:", event);
        showPlan(event.plan);
        addLog(`📋 Plano criado: ${event.plan.objective}`, 'info');
    });

    hubConnection.on("OnPlanUpdated", (event) => {
        console.log("Plan updated:", event);
        showPlan(event.plan);
        addLog(`🔄 Plano atualizado: ${event.reason}`, 'warning');
    });

    hubConnection.on("OnAgentStart", (event) => {
        console.log("Agent started:", event);
        setAgentWorking(event.agentName);
        updateStepStatus(event.agentName, 'running');
        addLog(`▶️ [${event.agentName}] Iniciando: ${event.action}`, 'agent');
    });

    hubConnection.on("OnAgentResult", (event) => {
        console.log("Agent result:", event);
        setAgentCompleted(event.agentName);
        updateStepStatus(event.agentName, 'completed');
        addLog(`✅ [${event.agentName}] Concluído`, 'success');
    });

    hubConnection.on("OnAgentError", (event) => {
        console.log("Agent error:", event);
        setAgentError(event.agentName);
        updateStepStatus(event.agentName, 'failed');
        addLog(`❌ [${event.agentName}] Erro: ${event.error}`, 'error');
    });

    hubConnection.on("OnReplan", (event) => {
        console.log("Replanning:", event);
        addLog(`🔄 Replanejando: ${event.reason}`, 'warning');
        showPlan(event.newPlan);
    });

    hubConnection.on("OnWorkflowCompleted", (event) => {
        console.log("Workflow completed:", event);
        if (event.success) {
            addLog(`🎉 Workflow concluído com sucesso!`, 'success');
            // Marcar todos os passos pendentes como concluídos
            markAllStepsCompleted();
            // Atualizar barra de progresso para 100%
            progressBar.style.width = '100%';
            // Atualizar badge do plano
            if (planStatus) {
                planStatus.textContent = 'Concluído ✅';
                planStatus.style.background = 'var(--success)';
            }
        } else {
            addLog(`❌ Workflow falhou: ${event.summary}`, 'error');
        }
        resetButtons();
    });

    hubConnection.on("OnStatusChanged", (event) => {
        console.log("Status changed:", event);
        if (planStatus) {
            planStatus.textContent = event.newStatus;
        }
    });

    hubConnection.on("OnLogMessage", (event) => {
        addLog(event.message, event.level.toLowerCase());
    });

    hubConnection.onreconnecting(() => {
        signalrStatus.innerHTML = '📡 Reconectando...';
        signalrStatus.className = 'status-badge status-warning';
    });

    hubConnection.onreconnected(() => {
        signalrStatus.innerHTML = '📡 Conectado';
        signalrStatus.className = 'status-badge status-online';
        if (currentTaskId) {
            hubConnection.invoke("SubscribeToTask", currentTaskId);
        }
    });

    hubConnection.onclose(() => {
        signalrStatus.innerHTML = '📡 Desconectado';
        signalrStatus.className = 'status-badge status-offline';
    });

    try {
        await hubConnection.start();
        signalrStatus.innerHTML = '📡 Conectado';
        signalrStatus.className = 'status-badge status-online';
        console.log("SignalR connected");
    } catch (err) {
        signalrStatus.innerHTML = '📡 Erro';
        signalrStatus.className = 'status-badge status-offline';
        console.error("SignalR connection error:", err);
    }
}

// ============================================
// PLAN DISPLAY
// ============================================
function showPlan(plan) {
    planSection.style.display = 'block';
    planStatus.textContent = plan.mode;
    
    planSteps.innerHTML = plan.steps.map(step => `
        <div class="plan-step" data-agent="${step.agentName}" data-status="pending">
            <span class="step-order">${step.order}</span>
            <span class="step-agent">${step.agentName}</span>
            <span class="step-action">${step.action}</span>
            <span class="step-status">⏳</span>
        </div>
    `).join('');
}

function updateStepStatus(agentName, status) {
    const steps = planSteps.querySelectorAll('.plan-step');
    let found = false;
    
    steps.forEach(stepEl => {
        const stepAgentText = stepEl.querySelector('.step-agent')?.textContent || '';
        if (stepEl.dataset.agent === agentName || stepAgentText === agentName) {
            stepEl.dataset.status = status;
            const statusEl = stepEl.querySelector('.step-status');
            switch(status) {
                case 'running': statusEl.textContent = '🔄'; break;
                case 'completed': statusEl.textContent = '✅'; break;
                case 'failed': statusEl.textContent = '❌'; break;
                case 'skipped': statusEl.textContent = '⏭️'; break;
                default: statusEl.textContent = '⏳';
            }
            found = true;
        }
    });
    
    updateProgress();
    return found;
}

// Marcar todos os passos como concluídos (quando o workflow termina)
function markAllStepsCompleted() {
    const steps = planSteps.querySelectorAll('.plan-step');
    steps.forEach(stepEl => {
        const currentStatus = stepEl.dataset.status;
        // Se ainda está pendente ou em execução, marcar como concluído
        if (currentStatus === 'pending' || currentStatus === 'running') {
            stepEl.dataset.status = 'completed';
            const statusEl = stepEl.querySelector('.step-status');
            statusEl.textContent = '✅';
            
            // Também atualizar o card do agente correspondente
            const agentName = stepEl.querySelector('.step-agent')?.textContent;
            if (agentName) {
                setAgentCompleted(agentName);
            }
        }
    });
    updateProgress();
}

function updateProgress() {
    const steps = planSteps.querySelectorAll('.plan-step');
    const completed = planSteps.querySelectorAll('[data-status="completed"]').length;
    const failed = planSteps.querySelectorAll('[data-status="failed"]').length;
    const total = steps.length;
    
    if (total > 0) {
        const progress = ((completed + failed) / total) * 100;
        progressBar.style.width = `${progress}%`;
        
        // Mudar cor se houver falha
        if (failed > 0) {
            progressBar.style.background = 'linear-gradient(90deg, var(--success), var(--danger))';
        }
    }
}

// ============================================
// RESPONSE DISPLAY
// ============================================
function showResponse(content) {
    responseSection.style.display = 'block';
    responseContent.textContent = content;
    currentResponse = content;
}

function hideResponse() {
    responseSection.style.display = 'none';
    responseContent.textContent = '';
    currentResponse = '';
}

// Copy response button
if (copyResponseBtn) {
    copyResponseBtn.addEventListener('click', () => {
        if (currentResponse) {
            navigator.clipboard.writeText(currentResponse).then(() => {
                copyResponseBtn.textContent = '✅ Copiado!';
                setTimeout(() => {
                    copyResponseBtn.textContent = '📋 Copiar';
                }, 2000);
            });
        }
    });
}

// ============================================
// HEALTH CHECK
// ============================================
async function checkHealth() {
    try {
        const response = await fetch(`${API_BASE_URL}/health`);
        if (response.ok) {
            const data = await response.json();
            statusIndicator.innerHTML = `🟢 Online v${data.version || '2.0'}`;
            statusIndicator.className = 'status-badge status-online';
            return true;
        }
    } catch (error) {
        statusIndicator.innerHTML = '🔴 Offline';
        statusIndicator.className = 'status-badge status-offline';
        return false;
    }
    return false;
}

// ============================================
// EXECUTE TASK
// ============================================
executeBtn.addEventListener('click', async () => {
    await executeTask('/run-task');
});

executeAutonomousBtn.addEventListener('click', async () => {
    await executeTask('/run-autonomous');
});

async function executeTask(endpoint) {
    const prompt = promptInput.value.trim();
    
    if (!prompt) {
        alert('Por favor, descreva o que você precisa!');
        return;
    }

    if (!(await checkHealth())) {
        addLog('❌ API offline. Verifique se a API está rodando.', 'error');
        return;
    }

    // Reset UI
    setButtonsLoading(true);
    clearLogs();
    resetAllAgents();
    hideResponse();
    planSection.style.display = 'none';

    try {
        console.log('Enviando requisição para:', `${API_BASE_URL}${endpoint}`);
        
        const response = await fetch(`${API_BASE_URL}${endpoint}`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ prompt })
        });

        if (!response.ok) {
            throw new Error(`Erro HTTP: ${response.status}`);
        }

        const data = await response.json();
        currentTaskId = data.taskId;
        
        addLog(`✨ Task iniciada: ${currentTaskId.substring(0, 8)}...`, 'info');
        if (data.mode === 'autonomous') {
            addLog(`🧠 Modo: Agentes Autônomos`, 'info');
        }
        addLog('━'.repeat(50), 'default');

        // Subscribe SignalR
        if (hubConnection && hubConnection.state === signalR.HubConnectionState.Connected) {
            await hubConnection.invoke("SubscribeToTask", currentTaskId);
        }

        // Connect SSE
        connectToStream(currentTaskId);
        cancelBtn.style.display = 'flex';

    } catch (error) {
        console.error('Erro:', error);
        addLog(`❌ Erro: ${error.message}`, 'error');
        resetButtons();
    }
}

// ============================================
// SSE STREAM
// ============================================
function connectToStream(taskId) {
    if (eventSource) {
        eventSource.close();
    }

    eventSource = new EventSource(`${API_BASE_URL}/stream/${taskId}`);
    let isCapturingResponse = false;
    let responseLines = [];

    eventSource.onmessage = (event) => {
        const data = event.data;

        if (data === '[DONE]') {
            addLog('━'.repeat(50), 'default');
            addLog('✅ Execução finalizada!', 'success');
            eventSource.close();
            
            // Marcar todos os passos como concluídos
            markAllStepsCompleted();
            
            // Atualizar barra de progresso para 100%
            progressBar.style.width = '100%';
            
            // Atualizar badge do plano
            if (planStatus) {
                planStatus.textContent = 'Concluído ✅';
                planStatus.style.background = 'var(--success)';
            }
            
            resetButtons();
            
            // Show captured response
            if (responseLines.length > 0) {
                showResponse(responseLines.join('\n'));
            }
            return;
        }

        // Detect response section
        if (data.includes('RESPOSTA DA IA:') || data.includes('RESULTADO DA REVISÃO:')) {
            isCapturingResponse = true;
            responseLines = [];
        }
        
        // Capture response lines
        if (isCapturingResponse && !data.includes('━')) {
            responseLines.push(data);
        }
        
        // Stop capturing on separator
        if (isCapturingResponse && data.includes('━') && responseLines.length > 0) {
            isCapturingResponse = false;
        }

        // Detectar atividade de agentes de forma mais precisa
        const agentPatterns = [
            { name: 'CodeGeneratorAgent', patterns: ['CodeGeneratorAgent', 'CodeGenerator'] },
            { name: 'ReviewerAgent', patterns: ['ReviewerAgent', 'Reviewer'] },
            { name: 'RAGAgent', patterns: ['RAGAgent', 'RAG'] },
            { name: 'AnalystAgent', patterns: ['AnalystAgent', 'Analyst'] },
            { name: 'WorkflowAgent', patterns: ['WorkflowAgent', 'Workflow'] },
            { name: 'SupervisorAgent', patterns: ['SupervisorAgent', 'Supervisor'] },
            { name: 'APIIntegrationAgent', patterns: ['APIIntegrationAgent', 'API'] }
        ];

        for (const agent of agentPatterns) {
            const matchesAgent = agent.patterns.some(p => data.includes(`[${p}]`) || data.includes(`${p} iniciado`));
            if (matchesAgent) {
                if (data.includes('iniciado') || data.includes('Iniciando') || data.includes('Processando') || data.includes('Analisando')) {
                    setAgentWorking(agent.name);
                    updateStepStatus(agent.name, 'running');
                } else if (data.includes('concluído') || data.includes('✅')) {
                    setAgentCompleted(agent.name);
                    updateStepStatus(agent.name, 'completed');
                } else if (data.includes('erro') || data.includes('❌')) {
                    setAgentError(agent.name);
                    updateStepStatus(agent.name, 'failed');
                }
                break;
            }
        }

        // Add to logs with appropriate styling
        let logType = 'default';
        if (data.includes('✅')) logType = 'success';
        else if (data.includes('❌')) logType = 'error';
        else if (data.includes('⚠️') || data.includes('🔄')) logType = 'warning';
        else if (data.includes('📝') || data.includes('🧠') || data.includes('📋')) logType = 'info';
        else if (data.includes('[') && data.includes('Agent]')) logType = 'agent';

        addLog(data, logType);
    };

    eventSource.onerror = () => {
        eventSource.close();
    };
}

// ============================================
// CANCEL TASK
// ============================================
cancelBtn.addEventListener('click', async () => {
    if (!currentTaskId) return;
    try {
        await fetch(`${API_BASE_URL}/task/${currentTaskId}/cancel`, { method: 'POST' });
        addLog('⚠️ Cancelamento solicitado...', 'warning');
    } catch (error) {
        console.error('Erro ao cancelar:', error);
    }
});

// ============================================
// UI HELPERS
// ============================================
function addLog(message, type = 'default') {
    const emptyState = logsContainer.querySelector('.logs-empty');
    if (emptyState) emptyState.remove();

    const logEntry = document.createElement('div');
    logEntry.className = `log-entry log-${type}`;
    logEntry.textContent = message;

    logsContainer.appendChild(logEntry);
    logsContainer.scrollTop = logsContainer.scrollHeight;
}

function clearLogs() {
    logsContainer.innerHTML = '';
}

function setButtonsLoading(loading) {
    executeBtn.disabled = loading;
    executeAutonomousBtn.disabled = loading;
    if (loading) {
        executeBtn.innerHTML = '<span class="btn-icon">⏳</span> Executando...';
        executeAutonomousBtn.innerHTML = '<span class="btn-icon">⏳</span> Executando...';
    } else {
        executeBtn.innerHTML = '<span class="btn-icon">🚀</span> Pipeline Simples';
        executeAutonomousBtn.innerHTML = '<span class="btn-icon">🧠</span> Agentes Autônomos';
    }
}

function resetButtons() {
    setButtonsLoading(false);
    cancelBtn.style.display = 'none';
    currentTaskId = null;
}

clearBtn.addEventListener('click', () => {
    clearLogs();
    hideResponse();
    resetAllAgents();
    planSection.style.display = 'none';
    logsContainer.innerHTML = `
        <div class="logs-empty">
            <p>👋 Aguardando execução...</p>
            <p class="logs-hint">Clique em um dos botões acima para começar</p>
        </div>
    `;
});

// Shortcut Ctrl+Enter
promptInput.addEventListener('keydown', (e) => {
    if (e.ctrlKey && e.key === 'Enter') {
        executeAutonomousBtn.click();
    }
});

// ============================================
// INITIALIZATION
// ============================================
checkHealth();
initSignalR();
initRAG();
setInterval(checkHealth, 10000);

// ============================================
// RAG FUNCTIONALITY
// ============================================
let ragSessionId = localStorage.getItem('ragSessionId') || generateSessionId();
localStorage.setItem('ragSessionId', ragSessionId);

function generateSessionId() {
    return 'rag_' + Date.now() + '_' + Math.random().toString(36).substr(2, 9);
}

function toggleRagPanel() {
    const panel = document.getElementById('ragPanel');
    if (panel.style.display === 'none') {
        panel.style.display = 'block';
        loadRagStats();
    } else {
        panel.style.display = 'none';
    }
}

function initRAG() {
    const dropZone = document.getElementById('dropZone');
    const fileInput = document.getElementById('fileInput');

    if (dropZone && fileInput) {
        // Drag and drop
        dropZone.addEventListener('dragover', (e) => {
            e.preventDefault();
            dropZone.classList.add('dragover');
        });

        dropZone.addEventListener('dragleave', () => {
            dropZone.classList.remove('dragover');
        });

        dropZone.addEventListener('drop', (e) => {
            e.preventDefault();
            dropZone.classList.remove('dragover');
            const files = e.dataTransfer.files;
            if (files.length > 0) {
                uploadFiles(files);
            }
        });

        // File input change
        fileInput.addEventListener('change', (e) => {
            if (e.target.files.length > 0) {
                uploadFiles(e.target.files);
            }
        });
    }

    // Atalho Enter para consulta RAG
    const ragQueryInput = document.getElementById('ragQueryInput');
    if (ragQueryInput) {
        ragQueryInput.addEventListener('keydown', (e) => {
            if (e.ctrlKey && e.key === 'Enter') {
                executeRagQuery();
            }
        });
    }

    // Load initial stats
    loadRagStats();
}

async function uploadFiles(files) {
    for (const file of files) {
        await uploadFile(file);
    }
    loadRagStats();
}

async function uploadFile(file) {
    const formData = new FormData();
    formData.append('file', file);
    formData.append('sessionId', ragSessionId);

    try {
        addLog(`📤 Fazendo upload: ${file.name}...`, 'info');
        
        const response = await fetch(`${API_BASE_URL}/rag/upload`, {
            method: 'POST',
            body: formData
        });

        const data = await response.json();
        
        if (data.success) {
            addLog(`✅ Upload concluído: ${file.name} (${data.stats.totalChunks} chunks)`, 'success');
            showUploadFeedback('success', `Documento "${file.name}" carregado com sucesso!`);
            showRagQuerySection(); // Mostrar seção de consulta
        } else {
            addLog(`❌ Erro no upload: ${data.error}`, 'error');
            showUploadFeedback('error', data.error);
        }
    } catch (error) {
        addLog(`❌ Erro no upload: ${error.message}`, 'error');
        showUploadFeedback('error', error.message);
    }
}

async function uploadText() {
    const textInput = document.getElementById('ragTextInput');
    const fileNameInput = document.getElementById('ragFileName');
    
    const content = textInput.value.trim();
    const fileName = fileNameInput.value.trim() || `texto_${Date.now()}.txt`;

    if (!content) {
        showUploadFeedback('error', 'Por favor, insira algum texto.');
        return;
    }

    try {
        addLog(`📝 Adicionando texto: ${fileName}...`, 'info');
        
        const response = await fetch(`${API_BASE_URL}/rag/upload-text`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                content: content,
                fileName: fileName,
                sessionId: ragSessionId
            })
        });

        const data = await response.json();
        
        if (data.success) {
            addLog(`✅ Texto adicionado: ${fileName}`, 'success');
            showUploadFeedback('success', `Texto "${fileName}" adicionado com sucesso!`);
            textInput.value = '';
            fileNameInput.value = '';
            loadRagStats();
            showRagQuerySection(); // Mostrar seção de consulta
        } else {
            addLog(`❌ Erro: ${data.error}`, 'error');
            showUploadFeedback('error', data.error);
        }
    } catch (error) {
        addLog(`❌ Erro: ${error.message}`, 'error');
        showUploadFeedback('error', error.message);
    }
}

async function loadRagStats() {
    try {
        const response = await fetch(`${API_BASE_URL}/rag/stats?sessionId=${ragSessionId}`);
        const data = await response.json();

        // Update stats display
        document.getElementById('statChunks').textContent = data.totalChunks || 0;
        document.getElementById('statChars').textContent = formatNumber(data.totalCharacters || 0);
        document.getElementById('statSources').textContent = data.sources?.length || 0;

        // Update sources list
        const sourcesList = document.getElementById('sourcesList');
        if (data.sources && data.sources.length > 0) {
            sourcesList.innerHTML = data.sources.map(s => 
                `<span class="source-item">📄 ${s}</span>`
            ).join('');
            showRagQuerySection(); // Mostrar seção de consulta se há documentos
        } else {
            sourcesList.innerHTML = '<p style="color: var(--text-muted); font-size: 0.85rem;">Nenhum documento carregado</p>';
            hideRagQuerySection(); // Esconder seção de consulta se não há documentos
        }

        // Update badge in header
        const ragDocsCount = document.getElementById('ragDocsCount');
        if (data.totalChunks > 0) {
            ragDocsCount.textContent = `📄 ${data.sources?.length || 0} docs`;
            ragDocsCount.style.display = 'inline';
        } else {
            ragDocsCount.style.display = 'none';
        }

    } catch (error) {
        console.error('Erro ao carregar stats do RAG:', error);
    }
}

function showRagQuerySection() {
    const section = document.getElementById('ragQuerySection');
    if (section) {
        section.style.display = 'block';
    }
}

function hideRagQuerySection() {
    const section = document.getElementById('ragQuerySection');
    if (section) {
        section.style.display = 'none';
    }
}

// ============================================
// CONSULTA RAG EXCLUSIVA
// ============================================
async function executeRagQuery() {
    const queryInput = document.getElementById('ragQueryInput');
    const query = queryInput.value.trim();

    if (!query) {
        alert('Por favor, digite uma pergunta!');
        return;
    }

    // Verificar se há documentos
    const statsResponse = await fetch(`${API_BASE_URL}/rag/stats?sessionId=${ragSessionId}`);
    const stats = await statsResponse.json();

    if (!stats.totalChunks || stats.totalChunks === 0) {
        addLog('⚠️ Nenhum documento carregado! Faça upload primeiro.', 'warning');
        alert('Nenhum documento carregado! Faça upload de um documento primeiro.');
        return;
    }

    // Desabilitar botão
    const btn = document.getElementById('ragQueryBtn');
    const originalText = btn.innerHTML;
    btn.disabled = true;
    btn.innerHTML = '<span class="btn-icon">⏳</span> Consultando documentos...';

    // Limpar logs e preparar UI
    clearLogs();
    hideResponse();
    setAgentWorking('RAGAgent');

    addLog('━'.repeat(50), 'default');
    addLog('📚 CONSULTA RAG EXCLUSIVA', 'info');
    addLog(`🔍 Pergunta: ${query}`, 'info');
    addLog(`📄 Documentos: ${stats.sources?.join(', ') || 'N/A'}`, 'info');
    addLog('━'.repeat(50), 'default');

    try {
        const response = await fetch(`${API_BASE_URL}/rag/query`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                query: query,
                sessionId: ragSessionId
            })
        });

        const data = await response.json();

        if (data.success) {
            addLog('━'.repeat(50), 'default');
            addLog('📄 RESPOSTA DOS DOCUMENTOS:', 'success');
            addLog('━'.repeat(50), 'default');
            
            // Mostrar resposta
            showResponse(data.answer);
            
            // Logar fontes usadas
            if (data.sources && data.sources.length > 0) {
                addLog(`📚 Fontes consultadas: ${data.sources.join(', ')}`, 'info');
            }
            
            addLog('━'.repeat(50), 'default');
            addLog('✅ Consulta RAG concluída!', 'success');
            
            setAgentCompleted('RAGAgent');
        } else {
            addLog(`❌ Erro: ${data.error}`, 'error');
            setAgentError('RAGAgent');
        }
    } catch (error) {
        addLog(`❌ Erro na consulta: ${error.message}`, 'error');
        setAgentError('RAGAgent');
    } finally {
        // Restaurar botão
        btn.disabled = false;
        btn.innerHTML = originalText;
    }
}

async function clearRagDocuments() {
    if (!confirm('Tem certeza que deseja remover todos os documentos?')) {
        return;
    }

    try {
        await fetch(`${API_BASE_URL}/rag/clear?sessionId=${ragSessionId}`, { method: 'DELETE' });
        addLog('🗑️ Documentos do RAG removidos', 'warning');
        loadRagStats();
        hideRagQuerySection();
        resetAllAgents();
    } catch (error) {
        console.error('Erro ao limpar documentos:', error);
    }
}

function showUploadFeedback(type, message) {
    const existingFeedback = document.querySelector('.upload-feedback');
    if (existingFeedback) {
        existingFeedback.remove();
    }

    const feedback = document.createElement('div');
    feedback.className = `upload-feedback upload-${type}`;
    feedback.textContent = message;

    const uploadArea = document.querySelector('.rag-upload-area');
    uploadArea.parentNode.insertBefore(feedback, uploadArea.nextSibling);

    setTimeout(() => feedback.remove(), 3000);
}

function formatNumber(num) {
    if (num >= 1000000) return (num / 1000000).toFixed(1) + 'M';
    if (num >= 1000) return (num / 1000).toFixed(1) + 'K';
    return num.toString();
}