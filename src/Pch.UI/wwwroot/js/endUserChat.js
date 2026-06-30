"use strict";
const RAW_SENTINELS = [
    "RAW_PROMPT",
    "RAW_PROVIDER_PAYLOAD",
    "APPROVAL_TOKEN",
    "HOLD_REFERENCE",
    "CANDIDATE_DISPLAY",
    "SECRET",
    "CREDENTIAL",
    "PAYMENT",
    "BOOKING_REF",
];
const candidates = [
    {
        id: "candidate-japan-classic-highlights",
        title: "Classic Japan highlights",
        mood: "reflective-culture",
        tone: "culture",
        summary: "Tokyo, Kyoto, and Osaka with cultural landmarks and local favorites.",
        evidence: "evidence-chat-route-a",
    },
    {
        id: "candidate-japan-reflective-culture",
        title: "Temple mornings and neighborhood evenings",
        mood: "reflective-culture",
        tone: "culture",
        summary: "A slower cultural route with calm mornings and local evening walks.",
        evidence: "evidence-chat-route-c",
    },
    {
        id: "candidate-japan-scenic-explorer",
        title: "Scenic Japan explorer",
        mood: "soft-nature",
        tone: "nature",
        summary: "Mountains, hot springs, and coastal towns for a quieter route.",
        evidence: "evidence-chat-route-b",
    },
];
const scenicCandidate = candidates[2];
const FALLBACK_DELAY_MS = 350;
function sanitizeText(value) {
    let sanitized = value.trim();
    for (const sentinel of RAW_SENTINELS) {
        sanitized = sanitized.replaceAll(sentinel, "[redacted]");
    }
    return sanitized.slice(0, 160);
}
function root() {
    return document.querySelector("[data-end-user-chat='v0']");
}
function transcript() {
    return document.querySelector("[data-transcript='trip']");
}
function scheduleFallback(shouldRun, action) {
    window.setTimeout(() => {
        if (shouldRun()) {
            action();
        }
    }, FALLBACK_DELAY_MS);
}
function setRootState(attrs) {
    const element = root();
    if (!element)
        return;
    for (const [key, value] of Object.entries(attrs)) {
        element.setAttribute(key, value);
    }
}
function appendTurn(id, role, kind, state, text, outcome, evidence, candidateId) {
    const panel = transcript();
    if (!panel || panel.querySelector(`[data-turn-id="${id}"]`)) {
        return null;
    }
    const article = document.createElement("article");
    article.className = "chat-turn";
    article.dataset.turnId = id;
    article.dataset.turnRole = role;
    article.dataset.turnKind = kind;
    article.dataset.turnState = state;
    article.dataset.outcomeCode = outcome;
    if (evidence)
        article.dataset.evidenceId = evidence;
    if (candidateId)
        article.dataset.candidateId = candidateId;
    article.innerHTML = `<div class="chat-turn__meta"><span>${role}</span><strong>${kind}</strong></div><p>${sanitizeText(text)}</p>`;
    panel.append(article);
    panel.dataset.turnCount = String(panel.querySelectorAll("[data-turn-id]").length);
    return article;
}
function candidateCard(candidate, state = "available") {
    return `<article class="candidate-card candidate-card--${candidate.tone}"
      data-candidate-id="${candidate.id}"
      data-candidate-category="trip-style"
      data-candidate-mood="${candidate.mood}"
      data-candidate-state="${state}"
      data-evidence-ids="evidence-chat-candidate,${candidate.evidence}">
      <span>${candidate.mood.replaceAll("-", " ")}</span>
      <h3>${candidate.title}</h3>
      <p>${candidate.summary}</p>
      <div class="candidate-actions">
        <button type="button" data-choice-action="select" data-candidate-id="${candidate.id}" aria-label="Select ${candidate.title}">Select</button>
        <button type="button" data-choice-action="defer" data-candidate-id="${candidate.id}" aria-label="Defer ${candidate.title}">Defer</button>
      </div>
    </article>`;
}
function ensureWorkObjects() {
    const panel = transcript();
    if (!panel || panel.querySelector("[data-form-id='form-trip-basics']")) {
        return;
    }
    panel.insertAdjacentHTML("beforeend", `<article class="work-bubble" data-work-bubble="form" data-form-id="form-trip-basics" data-form-state="draft">
      <p class="work-lead">I pulled the basics into a structured checkpoint before comparing options.</p>
      <section class="form-card">
        <h2>Trip basics</h2>
        <label class="form-field" data-field-id="field-trip-purpose" data-evidence-id="evidence-chat-purpose"><span>Purpose</span><input value="Vacation" readonly /></label>
        <label class="form-field" data-field-id="field-travel-style" data-evidence-id="evidence-chat-style"><span>Travel style</span><input value="Balanced culture and quiet time" readonly /></label>
        <button type="button" class="secondary-button" data-form-submit="form-trip-basics" aria-label="Submit trip basics form">Submit basics</button>
      </section>
    </article>
    <article class="work-bubble" data-work-bubble="choices" data-choice-set-id="choice-japan-style" data-choice-state="active">
      <p class="work-lead">Here are option decks grouped by feel.</p>
      <section class="choice-card">
        <div class="choice-card__header"><h2>Choose an itinerary direction</h2><div class="deck-controls" data-deck-controls="mood-deck"><button type="button" data-deck-control="previous" aria-label="Show previous culture option">‹</button><span data-deck-index="0">1/2</span><button type="button" data-deck-control="next" aria-label="Show next culture option">›</button></div></div>
        <div class="candidate-deck" data-candidate-deck="reflective-culture" data-deck-index="0" tabindex="0">${candidates.slice(0, 2).map((candidate) => candidateCard(candidate)).join("")}</div>
        ${candidateCard(scenicCandidate)}
      </section>
    </article>
    <article class="approval-plate" data-approval-id="approval-preview-mock-hold" data-approval-state="not_requested" data-approval-outcome="not_requested">
      <div><span>Commitment checkpoint</span><h2>Mock hold preview</h2><p>No hold, booking, payment, or live provider handoff can run without approval.</p></div>
      <button type="button" data-approval-action="request" aria-label="Request mock hold approval preview">Preview approval block</button>
    </article>
    <aside class="provider-notice" data-provider-failure-notice="notice-deterministic-fallback" data-provider-outcome="deterministic_fallback_active" data-provider-state="deterministic"><strong>Provider status</strong><p>Live provider calls are disabled for required smoke; the deterministic transcript remains available.</p></aside>
    <section class="plan-trail" data-plan-trail="chat" tabindex="0" aria-label="Evidence and plan trail">
      <article data-plan-trail-item="trail-mission-facts" data-plan-trail-kind="mission" data-plan-trail-state="accepted" data-evidence-id="evidence-chat-purpose" data-trace-outcome="golden_trace_complete"><span>mission</span><strong>Mission facts accepted</strong></article>
      <article data-plan-trail-item="trail-pending-confirmations" data-plan-trail-kind="confirmation" data-plan-trail-state="pending" data-evidence-id="evidence-chat-style" data-trace-outcome="end_user_chat_pending_confirmation"><span>confirmation</span><strong>Travel style and dates pending</strong></article>
    </section>`);
}
function sendPrompt() {
    const prompt = document.querySelector("[data-prompt-entry='trip'], [data-prompt-entry='trip-drawer']");
    const promptLength = prompt?.value.trim().length ?? 0;
    document.querySelector("[data-composer-layout='expanded_start']")?.setAttribute("hidden", "");
    setRootState({
        "data-final-state": "applied",
        "data-composer-state": "collapsed_drawer",
        "data-ask-drawer": "closed",
        "data-provider-outcome": "deterministic_fallback_active",
    });
    appendTurn("turn-user-1", "user", "prompt", "submitted", `Trip request accepted with ${promptLength} characters. Raw prompt text is kept out of transcript storage.`, "prompt_received");
    appendTurn("turn-provider-role-status", "provider", "role-status", "applied", "Offline deterministic model role is active; live provider roles are disabled for this run.", "model_role_status_ready");
    appendTurn("turn-assistant-final", "assistant", "final", "applied", "Final deterministic trip plan is ready with canonical evidence markers.", "golden_trace_complete", "evidence-chat-purpose");
    ensureWorkObjects();
    if (!document.querySelector("[data-ask-action='open']")) {
        document.querySelector(".chat-main")?.insertAdjacentHTML("beforeend", `<button type="button" class="ask-tab" data-ask-action="open" aria-label="Open Ask drawer">Ask</button>`);
    }
}
function submitForm() {
    document.querySelector("[data-form-id='form-trip-basics']")?.setAttribute("data-form-state", "accepted");
    setRootState({ "data-final-state": "form_submitted" });
    appendTurn("turn-form-submitted", "user", "form", "accepted", "Trip basics were submitted and accepted by the deterministic harness path.", "form_card_accepted", "evidence-chat-purpose");
}
function selectCandidate(candidateId) {
    const candidate = candidates.find((item) => item.id === candidateId);
    if (!candidate)
        return;
    document.querySelectorAll("[data-candidate-id]").forEach((element) => {
        if (element instanceof HTMLElement && element.dataset.candidateId === candidateId) {
            element.dataset.candidateState = "selected";
        }
    });
    setRootState({ "data-final-state": "candidate_selected" });
    const article = appendTurn("turn-choice-selected", "user", "choice", "selected", "", "choice_candidate_selected", "evidence-chat-candidate", candidateId);
    if (article) {
        article.dataset.candidateCategory = "trip-style";
        article.innerHTML = `<div class="chat-turn__meta"><span>user</span><strong>choice</strong></div><div class="selected-option-bubble candidate-card candidate-card--${candidate.tone}" data-selected-option-card="true" data-candidate-id="${candidate.id}" data-candidate-category="trip-style" data-candidate-mood="${candidate.mood}" data-candidate-state="selected" data-evidence-ids="evidence-chat-candidate,${candidate.evidence}"><span>${candidate.mood.replaceAll("-", " ")}</span><h3>${candidate.title}</h3><p>${candidate.summary}</p></div>`;
    }
    document.querySelector("[data-plan-trail='chat']")?.insertAdjacentHTML("beforeend", `<article data-plan-trail-item="trail-selected-option" data-plan-trail-kind="selected-option" data-plan-trail-state="selected" data-candidate-id="${candidate.id}" data-evidence-id="${candidate.evidence}" data-trace-outcome="choice_candidate_selected"><span>selected option</span><strong>${candidate.title}</strong></article>`);
}
function deferCandidate(candidateId) {
    const candidate = candidates.find((item) => item.id === candidateId);
    if (!candidate)
        return;
    setRootState({ "data-final-state": "candidate_deferred" });
    document.querySelector("[data-plan-trail='chat']")?.insertAdjacentHTML("beforeend", `<article data-plan-trail-item="trail-deferred-option" data-plan-trail-kind="deferred-option" data-plan-trail-state="deferred" data-candidate-id="${candidate.id}" data-evidence-id="${candidate.evidence}" data-trace-outcome="choice_candidate_deferred"><span>deferred option</span><strong>${candidate.title}</strong></article>`);
}
function requestApproval() {
    setRootState({ "data-final-state": "blocked", "data-approval-state": "blocked_missing_approval", "data-error-code": "PCH_UI_CHAT_APPROVAL_REQUIRED", "data-blocked-reason": "approval_required_preview" });
    const plate = document.querySelector("[data-approval-id='approval-preview-mock-hold']");
    plate?.setAttribute("data-approval-state", "blocked_missing_approval");
    plate?.setAttribute("data-blocked-reason", "approval_required_preview");
    appendTurn("turn-approval-blocked", "harness", "approval", "blocked", "Mock hold preparation is blocked until explicit approval is available. No hold or booking was created.", "approval_required_preview", "evidence-chat-approval");
    document.querySelector("[data-plan-trail='chat']")?.insertAdjacentHTML("beforeend", `<article data-plan-trail-item="trail-approval-blocked" data-plan-trail-kind="approval" data-plan-trail-state="blocked" data-evidence-id="evidence-chat-approval" data-trace-outcome="approval_required_preview"><span>approval</span><strong>Mock hold approval blocked</strong></article>`);
}
function toggleDrawer(open) {
    setRootState({ "data-ask-drawer": open ? "open" : "closed", "data-composer-state": open ? "collapsed_drawer_open" : "collapsed_drawer" });
    document.querySelector("[data-ask-drawer-panel]")?.remove();
    if (!open)
        return;
    document.querySelector(".chat-main")?.insertAdjacentHTML("beforeend", `<aside class="ask-drawer" data-ask-drawer-panel="open" aria-label="Ask drawer"><div><h2>Ask the agent</h2><button type="button" data-ask-action="close" aria-label="Close Ask drawer">Close</button></div><label for="trip-prompt-drawer">Follow-up prompt</label><textarea id="trip-prompt-drawer" data-prompt-entry="trip-drawer" aria-label="Follow-up trip prompt" rows="5"></textarea><button type="button" class="send-button" data-send-action="deterministic-drawer" aria-label="Send deterministic follow-up prompt">Send</button></aside>`);
}
function moveDeck(direction) {
    const deck = document.querySelector("[data-candidate-deck='reflective-culture']");
    const index = document.querySelector("[data-deck-index]");
    if (!deck || !index)
        return;
    const next = (Number(deck.dataset.deckIndex ?? "0") + direction + 2) % 2;
    deck.dataset.deckIndex = String(next);
    index.dataset.deckIndex = String(next);
    index.textContent = `${next + 1}/2`;
    deck.scrollLeft = next * 260;
}
document.addEventListener("click", (event) => {
    const target = event.target;
    if (!(target instanceof HTMLElement))
        return;
    const action = target.dataset;
    if (action.sendAction === "deterministic" || action.sendAction === "deterministic-drawer") {
        const beforeFinalState = root()?.dataset.finalState;
        const beforeTurnCount = transcript()?.dataset.turnCount;
        scheduleFallback(() => root()?.dataset.finalState === beforeFinalState && transcript()?.dataset.turnCount === beforeTurnCount, sendPrompt);
    }
    if (action.formSubmit === "form-trip-basics") {
        scheduleFallback(() => document.querySelector("[data-form-id='form-trip-basics']")?.dataset.formState !== "accepted", submitForm);
    }
    if (action.choiceAction === "select" && action.candidateId) {
        const candidateId = action.candidateId;
        scheduleFallback(() => document.querySelector(`[data-selected-option-card='true'][data-candidate-id='${candidateId}']`) === null, () => selectCandidate(candidateId));
    }
    if (action.choiceAction === "defer" && action.candidateId) {
        const candidateId = action.candidateId;
        scheduleFallback(() => document.querySelector(`[data-plan-trail-item='trail-deferred-option'][data-candidate-id='${candidateId}']`) === null, () => deferCandidate(candidateId));
    }
    if (action.approvalAction === "request") {
        scheduleFallback(() => root()?.dataset.approvalState !== "blocked_missing_approval", requestApproval);
    }
    if (action.askAction === "open") {
        scheduleFallback(() => root()?.dataset.askDrawer !== "open", () => toggleDrawer(true));
    }
    if (action.askAction === "close") {
        scheduleFallback(() => root()?.dataset.askDrawer !== "closed", () => toggleDrawer(false));
    }
    if (action.deckControl === "next" || action.deckControl === "previous") {
        const beforeIndex = document.querySelector("[data-candidate-deck='reflective-culture']")?.dataset.deckIndex;
        const direction = action.deckControl === "next" ? 1 : -1;
        scheduleFallback(() => document.querySelector("[data-candidate-deck='reflective-culture']")?.dataset.deckIndex === beforeIndex, () => moveDeck(direction));
    }
});
document.addEventListener("keydown", (event) => {
    const target = event.target;
    if (!(target instanceof HTMLElement) || target.dataset.planTrail !== "chat")
        return;
    if (event.key === "ArrowRight") {
        target.scrollLeft += 180;
        target.dataset.planTrailBrowse = "keyboard-next";
        event.preventDefault();
    }
    if (event.key === "ArrowLeft") {
        target.scrollLeft -= 180;
        target.dataset.planTrailBrowse = "keyboard-previous";
        event.preventDefault();
    }
});
//# sourceMappingURL=endUserChat.js.map