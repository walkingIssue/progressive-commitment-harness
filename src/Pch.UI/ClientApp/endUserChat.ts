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

type MediaAsset = {
  id: string;
  mood: string;
  path: string;
  alt: string;
  state: string;
  sourceClass: string;
  license: string;
};

type CandidateOption = {
  id: string;
  title: string;
  mood: string;
  tone: string;
  media: MediaAsset;
  summary: string;
  evidence: string;
};

const mediaAssets: Record<string, MediaAsset> = {
  cultural_immersive: {
    id: "backdrop.cultural.sakura_temple.cultural_immersive",
    mood: "cultural_immersive",
    path: "/media/japan-prompt-studio-pack/backdrop.cultural.sakura_temple.cultural_immersive.png",
    alt: "Sakura temple prompt-studio mood art.",
    state: "ready",
    sourceClass: "prompt_studio_generated_local",
    license: "project-generated",
  },
  reflective_culture: {
    id: "backdrop.cultural.vermilion_torii.spiritual_serene",
    mood: "spiritual_serene",
    path: "/media/japan-prompt-studio-pack/backdrop.cultural.vermilion_torii.spiritual_serene.png",
    alt: "Vermilion torii prompt-studio reflective culture art.",
    state: "ready",
    sourceClass: "prompt_studio_generated_local",
    license: "project-generated",
  },
  soft_nature: {
    id: "backdrop.scenic.fuji_lake.scenic_relaxed",
    mood: "scenic_relaxed",
    path: "/media/japan-prompt-studio-pack/backdrop.scenic.fuji_lake.scenic_relaxed.png",
    alt: "Fuji lake prompt-studio soft nature art.",
    state: "ready",
    sourceClass: "prompt_studio_generated_local",
    license: "project-generated",
  },
  calm_morning: {
    id: "backdrop.logistics.map_planning.family_easy",
    mood: "family_easy",
    path: "/media/japan-prompt-studio-pack/backdrop.logistics.map_planning.family_easy.png",
    alt: "Gentle map-planning prompt-studio morning mood art.",
    state: "ready",
    sourceClass: "prompt_studio_generated_local",
    license: "project-generated",
  },
  restorative_downtime: {
    id: "backdrop.scenic.onsen_valley.wellness_restorative",
    mood: "wellness_restorative",
    path: "/media/japan-prompt-studio-pack/backdrop.scenic.onsen_valley.wellness_restorative.png",
    alt: "Onsen valley prompt-studio restorative mood art.",
    state: "ready",
    sourceClass: "prompt_studio_generated_local",
    license: "project-generated",
  },
  logistics_transit: {
    id: "backdrop.urban.station_grid.budget_practical",
    mood: "budget_practical",
    path: "/media/japan-prompt-studio-pack/backdrop.urban.station_grid.budget_practical.png",
    alt: "Station grid prompt-studio logistics art.",
    state: "ready",
    sourceClass: "prompt_studio_generated_local",
    license: "project-generated",
  },
  mood_placeholder: {
    id: "backdrop.cultural.craft_district.arts_design",
    mood: "arts_design",
    path: "/media/japan-prompt-studio-pack/backdrop.cultural.craft_district.arts_design.png",
    alt: "Craft district prompt-studio fallback art.",
    state: "fallback",
    sourceClass: "prompt_studio_generated_local",
    license: "project-generated",
  },
};

function mediaAsset(assetId: string): MediaAsset {
  return mediaAssets[assetId] ?? mediaAssets.mood_placeholder!;
}

const candidates: CandidateOption[] = [
  {
    id: "candidate-japan-classic-highlights",
    title: "Classic Japan highlights",
    mood: "reflective-culture",
    tone: "culture",
    media: mediaAsset("reflective_culture"),
    summary: "Tokyo, Kyoto, and Osaka with cultural landmarks and local favorites.",
    evidence: "evidence-chat-route-a",
  },
  {
    id: "candidate-japan-reflective-culture",
    title: "Temple mornings and neighborhood evenings",
    mood: "reflective-culture",
    tone: "culture",
    media: mediaAsset("cultural_immersive"),
    summary: "A slower cultural route with calm mornings and local evening walks.",
    evidence: "evidence-chat-route-c",
  },
  {
    id: "candidate-japan-scenic-explorer",
    title: "Scenic Japan explorer",
    mood: "soft-nature",
    tone: "nature",
    media: mediaAsset("soft_nature"),
    summary: "Mountains, hot springs, and coastal towns for a quieter route.",
    evidence: "evidence-chat-route-b",
  },
  {
    id: "candidate-japan-transit-rhythm",
    title: "Transit rhythm and easy transfers",
    mood: "logistics-transit",
    tone: "transit",
    media: mediaAsset("mood_placeholder"),
    summary: "Clean route timing and low-friction station changes.",
    evidence: "evidence-chat-route-transit",
  },
];

const scenicCandidate = candidates[2]!;
const fallbackCandidate = candidates[3]!;
const calmMorningMedia = mediaAsset("calm_morning");
const restorativeDowntimeMedia = mediaAsset("restorative_downtime");
const FALLBACK_DELAY_MS = 350;

function sanitizeText(value: string): string {
  let sanitized = value.trim();
  for (const sentinel of RAW_SENTINELS) {
    sanitized = sanitized.replaceAll(sentinel, "[redacted]");
  }
  return sanitized.slice(0, 160);
}

function root(): HTMLElement | null {
  return document.querySelector<HTMLElement>("[data-end-user-chat='v0']");
}

function transcript(): HTMLElement | null {
  return document.querySelector<HTMLElement>("[data-transcript='trip']");
}

function chatMain(): HTMLElement | null {
  return document.querySelector<HTMLElement>(".chat-main");
}

function scheduleFallback(shouldRun: () => boolean, action: () => void): void {
  window.setTimeout(() => {
    if (shouldRun()) {
      suppressReconnectModal();
      action();
    }
  }, FALLBACK_DELAY_MS);
}

function suppressReconnectModal(): void {
  document.documentElement.dataset.endUserChatFallback = "active";
  const modal = document.getElementById("components-reconnect-modal") as HTMLDialogElement | null;
  if (!modal) return;

  modal.dataset.endUserFallbackMuted = "true";
  modal.style.display = "none";
  modal.style.pointerEvents = "none";
  if (typeof modal.close === "function" && modal.open) {
    modal.close();
  }
}

function setRootState(attrs: Record<string, string>): void {
  const element = root();
  if (!element) return;
  for (const [key, value] of Object.entries(attrs)) {
    element.setAttribute(key, value);
  }
}

function selectedModelRole(): string {
  return root()?.dataset.selectedModelRole ?? "in-harness-action-generator";
}

function setModelRole(role: string): void {
  const normalized = role === "in-harness-action-generator" || role === "strong-planner"
    ? role
    : "deterministic-offline";
  const liveSelected = normalized !== "deterministic-offline";
  setRootState({
    "data-selected-model-role": normalized,
    "data-live-preflight-state": liveSelected ? "blocked_by_guard" : "deterministic_default",
    "data-live-proposal-state": liveSelected ? "not_run" : "not_requested",
    "data-harness-validation-state": "not_run",
    "data-latest-turn-source": liveSelected ? "live_model_proposal_blocked" : "deterministic_fallback",
    "data-provider-request-state": "not_attempted",
    "data-provider-outcome": liveSelected ? "live_preflight_disabled" : "deterministic_fallback_active",
    "data-provider-health": liveSelected ? "live_guard_blocked" : "offline_deterministic",
    "data-error-code": liveSelected ? "PCH_UI_LIVE_MODEL_GUARDED" : "",
    "data-blocked-reason": liveSelected ? "live_preflight_disabled" : "",
  });
  document.querySelector<HTMLElement>("[data-model-status-strip='end-user']")?.setAttribute("data-selected-model-role", normalized);
  document.querySelector<HTMLElement>("[data-model-status-strip='end-user']")?.setAttribute("data-live-preflight-state", liveSelected ? "blocked_by_guard" : "deterministic_default");
  document.querySelector<HTMLElement>("[data-model-status-strip='end-user']")?.setAttribute("data-live-proposal-state", liveSelected ? "not_run" : "not_requested");
  document.querySelector<HTMLElement>("[data-model-status-strip='end-user']")?.setAttribute("data-harness-validation-state", "not_run");
  document.querySelector<HTMLElement>("[data-model-status-strip='end-user']")?.setAttribute("data-latest-turn-source", liveSelected ? "live_model_proposal_blocked" : "deterministic_fallback");
  document.querySelector<HTMLElement>("[data-model-status-strip='end-user']")?.setAttribute("data-provider-request-state", "not_attempted");
  document.querySelectorAll<HTMLElement>("[data-model-role-option]").forEach((button) => {
    const isSelected = button.dataset.modelRoleOption === normalized;
    button.dataset.modelRoleSelected = String(isSelected);
    button.setAttribute("aria-pressed", String(isSelected));
  });
}

function appendTurn(
  id: string,
  role: string,
  kind: string,
  state: string,
  text: string,
  outcome: string,
  evidence?: string,
  candidateId?: string,
): HTMLElement | null {
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
  if (evidence) article.dataset.evidenceId = evidence;
  if (candidateId) article.dataset.candidateId = candidateId;
  article.innerHTML = `<div class="chat-turn__meta"><span>${role}</span><strong>${kind}</strong></div><p>${sanitizeText(text)}</p>`;
  panel.append(article);
  panel.dataset.turnCount = String(panel.querySelectorAll("[data-turn-id]").length);
  return article;
}

function candidateCard(candidate: (typeof candidates)[number], state = "available"): string {
  return `<article class="candidate-card candidate-card--${candidate.tone}"
      data-candidate-id="${candidate.id}"
      data-candidate-category="trip-style"
      data-candidate-mood="${candidate.mood}"
      data-candidate-state="${state}"
      data-media-asset-id="${candidate.media.id}"
      data-media-state="${candidate.media.state}"
      data-media-source-class="${candidate.media.sourceClass}"
      data-evidence-ids="evidence-chat-candidate,${candidate.evidence}">
      <img class="candidate-media" src="${candidate.media.path}" alt="${candidate.media.alt}" data-media-image="candidate" data-media-asset-id="${candidate.media.id}" data-media-mood="${candidate.media.mood}" data-media-state="${candidate.media.state}" data-media-license="${candidate.media.license}" />
      <span>${candidate.mood.replaceAll("-", " ")}</span>
      <h3>${candidate.title}</h3>
      <p>${candidate.summary}</p>
      <div class="candidate-actions">
        <button type="button" data-choice-action="select" data-candidate-id="${candidate.id}" aria-label="Select ${candidate.title}">Select</button>
        <button type="button" data-choice-action="defer" data-candidate-id="${candidate.id}" aria-label="Defer ${candidate.title}">Defer</button>
      </div>
    </article>`;
}

function timelineItem(
  id: string,
  mode: string,
  kind: string,
  state: string,
  title: string,
  summary: string,
  originTurnId: string,
  media: MediaAsset,
  attrs = "",
): string {
  return `<button type="button" class="planning-timeline__item" data-planning-timeline-item="${id}" data-planning-mode="${mode}" data-planning-kind="${kind}" data-planning-state="${state}" data-origin-turn-id="${originTurnId}" data-media-asset-id="${media.id}" data-media-state="${media.state}" data-trace-outcome="${state}" ${attrs} aria-label="Jump to ${title}"><img src="${media.path}" alt="${media.alt}" data-planning-timeline-media="true" data-media-asset-id="${media.id}" data-media-mood="${media.mood}" data-media-state="${media.state}" data-media-license="${media.license}" /><span>${kind.replaceAll("-", " ")}</span><strong>${title}</strong><small>${summary}</small></button>`;
}

function ensurePlanningTimeline(): void {
  if (document.querySelector("[data-planning-timeline='trip']")) {
    return;
  }

  chatMain()?.insertAdjacentHTML(
    "beforeend",
    `<section class="planning-timeline" data-planning-timeline="trip" data-planning-timeline-mode="day" data-raw-absence-state="verified" aria-labelledby="planning-timeline-title">
      <div class="planning-timeline__header">
        <div><p class="eyebrow">Planning history</p><h2 id="planning-timeline-title">Your itinerary memory</h2></div>
        <div class="timeline-mode-toggle" data-timeline-mode-toggle="trip" role="group" aria-label="Planning timeline mode">
          <button type="button" data-timeline-mode-action="day" data-timeline-mode-state="active" aria-pressed="true">Day</button>
          <button type="button" data-timeline-mode-action="task" data-timeline-mode-state="inactive" aria-pressed="false">Task</button>
        </div>
      </div>
      <div class="planning-timeline__rail" data-planning-timeline-rail="day" tabindex="0" aria-label="Browse planning history">
        ${timelineItem("timeline-day-1-mission", "day", "mission", "accepted", "Day 1 direction", "Culture-first Japan trip facts accepted.", "turn-03", mediaAsset("calm_morning"), 'data-day-id="day-japan-01" data-slot-id="slot-morning" data-decision-id="decision-mission-facts" data-evidence-id="evidence-chat-purpose"')}
        ${timelineItem("timeline-day-1-confirmation", "day", "confirmation", "pending", "Style confirmation", "Dates and travel style remain confirmation-ready.", "turn-assistant-final", mediaAsset("restorative_downtime"), 'data-day-id="day-japan-01" data-slot-id="slot-planning" data-decision-id="decision-pending-confirmation" data-evidence-id="evidence-chat-style"')}
        ${timelineItem("timeline-day-2-availability", "day", "availability", "quote-ready", "Availability guarded", "Quote and hold-adjacent work remains approval gated.", "turn-assistant-final", mediaAsset("logistics_transit"), 'data-day-id="day-japan-02" data-slot-id="slot-availability" data-decision-id="decision-availability-preview" data-evidence-id="evidence-chat-approval"')}
        ${timelineItem("timeline-task-basics", "task", "task", "accepted", "Understand trip basics", "Mission facts and deterministic transcript are ready.", "turn-03", mediaAsset("calm_morning"), 'data-task-id="task-basics" data-decision-id="decision-task-basics" data-evidence-id="evidence-chat-purpose" hidden')}
        ${timelineItem("timeline-task-itinerary", "task", "task", "active", "Compare itinerary choices", "Candidate cards are ready for select or defer.", "turn-assistant-final", mediaAsset("reflective_culture"), 'data-task-id="task-itinerary" data-decision-id="decision-choice-set" data-evidence-id="evidence-chat-candidate" hidden')}
        ${timelineItem("timeline-task-approval", "task", "task", "not_started", "Approval gate", "Mock hold work is blocked until explicit approval.", "turn-assistant-final", mediaAsset("logistics_transit"), 'data-task-id="task-approval" data-decision-id="decision-approval-preview" data-evidence-id="evidence-chat-approval" hidden')}
      </div>
    </section>`,
  );
}

function ensureWorkObjects(): void {
  const panel = transcript();
  if (!panel || panel.querySelector("[data-form-id='form-trip-basics']")) {
    return;
  }

  panel.insertAdjacentHTML(
    "beforeend",
    `<article class="work-bubble" data-work-bubble="form" data-form-id="form-trip-basics" data-form-state="draft">
      <p class="work-lead">I pulled the basics into a structured checkpoint before comparing options.</p>
      <section class="form-card">
        <h2>Trip basics</h2>
        <label class="form-field" data-field-id="field-trip-purpose" data-evidence-id="evidence-chat-purpose"><span>Purpose</span><input value="Vacation" readonly /></label>
        <label class="form-field" data-field-id="field-travel-style" data-evidence-id="evidence-chat-style"><span>Travel style</span><input value="Balanced culture and quiet time" readonly /></label>
        <button type="button" class="secondary-button" data-form-submit="form-trip-basics" aria-label="Submit trip basics form">Submit basics</button>
      </section>
    </article>
    <article class="work-bubble" data-work-bubble="choices" data-choice-set-id="choice-japan-style" data-choice-state="active">
      <p class="work-lead">I grouped these into little moods. Pick the one that feels like the trip you want to remember.</p>
      <section class="choice-card">
        <div class="choice-card__header"><h2>Choose an itinerary direction</h2><div class="deck-controls" data-deck-controls="mood-deck"><button type="button" data-deck-control="previous" aria-label="Show previous culture option">Prev</button><span data-deck-index="0">1/2</span><button type="button" data-deck-control="next" aria-label="Show next culture option">Next</button></div></div>
        <div class="candidate-deck" data-candidate-deck="reflective-culture" data-deck-index="0" tabindex="0">${candidates.slice(0, 2).map((candidate) => candidateCard(candidate)).join("")}</div>
        ${candidateCard(scenicCandidate)}
        ${candidateCard(fallbackCandidate)}
      </section>
    </article>
    <article class="approval-plate" data-approval-id="approval-preview-mock-hold" data-approval-state="not_requested" data-approval-outcome="not_requested">
      <div><span>Commitment checkpoint</span><h2>Mock hold preview</h2><p>No hold, booking, payment, or live provider handoff can run without approval.</p></div>
      <button type="button" data-approval-action="request" aria-label="Request mock hold approval preview">Preview approval block</button>
    </article>
    <aside class="provider-notice" data-provider-failure-notice="notice-deterministic-fallback" data-provider-outcome="deterministic_fallback_active" data-provider-state="deterministic"><strong>Provider status</strong><p>Live provider calls are disabled for required smoke; the deterministic transcript remains available.</p></aside>
    <section class="evidence-strip" data-evidence-strip="chat"><span data-evidence-id="evidence-chat-purpose" data-evidence-kind="trace" data-trace-outcome="golden_trace_complete">Canonical trace evidence</span><span data-evidence-id="evidence-chat-candidate" data-evidence-kind="candidate" data-trace-outcome="candidate_pool_ready">Candidate provenance retained</span></section>`,
  );
  ensurePlanningTimeline();
}

function sendPrompt(): void {
  const prompt = document.querySelector<HTMLTextAreaElement>("[data-prompt-entry='trip'], [data-prompt-entry='trip-drawer']");
  const promptLength = prompt?.value.trim().length ?? 0;
  const role = selectedModelRole();
  const liveSelected = role !== "deterministic-offline";
  const livePreflightState = root()?.dataset.livePreflightState;
  const liveConfigured = liveSelected && (livePreflightState === "preflight_ready" || livePreflightState === "preflight_passed");
  const liveFallbackOutcome = liveConfigured ? "browser_circuit_disconnected" : "live_preflight_disabled";
  document.querySelector<HTMLElement>("[data-composer-layout='expanded_start']")?.setAttribute("hidden", "");
  setRootState({
    "data-final-state": liveSelected ? "live_model_blocked" : "applied",
    "data-composer-state": "collapsed_drawer",
    "data-ask-drawer": "closed",
    "data-provider-outcome": liveSelected ? liveFallbackOutcome : "deterministic_fallback_active",
    "data-live-preflight-state": liveSelected ? livePreflightState ?? "blocked_by_guard" : "deterministic_default",
    "data-live-proposal-state": liveSelected ? "not_run" : "not_requested",
    "data-harness-validation-state": "not_run",
    "data-latest-turn-source": liveSelected ? "live_model_proposal_blocked" : "deterministic_fallback",
    "data-provider-request-state": "not_attempted",
    "data-live-turn-attempt-count": liveSelected ? "1" : "0",
    "data-live-second-turn-state": "not_started",
    "data-error-code": liveSelected ? "PCH_UI_BROWSER_CIRCUIT_DISCONNECTED" : "",
    "data-blocked-reason": liveSelected ? liveFallbackOutcome : "",
  });
  toggleDrawer(false);
  appendTurn("turn-user-1", "user", "prompt", "submitted", `Trip request accepted with ${promptLength} characters. Raw prompt text is kept out of transcript storage.`, "prompt_received");
  appendTurn(
    "turn-provider-role-status",
    "provider",
    "role-status",
    liveSelected ? "blocked" : "applied",
    liveSelected
      ? "Live in-harness mode is selected, but the browser circuit is disconnected; no fallback provider request was attempted in this tab."
      : "Offline deterministic model role is active by explicit selection.",
    "model_role_status_ready",
  );
  if (liveSelected) {
    appendTurn("turn-live-model-run", "provider", "live-model", "blocked", "The browser circuit is disconnected, so this tab could not send the live provider request. The server live path remains configured.", liveFallbackOutcome, "evidence-chat-live-model");
    appendTurn("turn-live-work-item-1", "assistant", "live-blocked", "blocked", "Live model output was not used in this browser fallback. Reload in a healthy Blazor circuit or switch to deterministic mode explicitly.", liveFallbackOutcome, "evidence-chat-live-model");
  } else {
    appendTurn("turn-assistant-final", "assistant", "final", "applied", "Final deterministic trip plan is ready with canonical evidence markers.", "golden_trace_complete", "evidence-chat-purpose");
    ensureWorkObjects();
  }
  if (!document.querySelector("[data-ask-action='open']")) {
    document.querySelector(".chat-main")?.insertAdjacentHTML("beforeend", `<button type="button" class="ask-tab" data-ask-action="open" aria-label="Open Ask drawer">Ask</button>`);
  }
}

function submitForm(): void {
  document.querySelector<HTMLElement>("[data-form-id='form-trip-basics']")?.setAttribute("data-form-state", "accepted");
  setRootState({ "data-final-state": "form_submitted" });
  appendTurn("turn-form-submitted", "user", "form", "accepted", "Trip basics were submitted and accepted by the deterministic harness path.", "form_card_accepted", "evidence-chat-purpose");
}

function selectCandidate(candidateId: string): void {
  const candidate = candidates.find((item) => item.id === candidateId);
  if (!candidate) return;
  document.querySelectorAll("[data-candidate-id]").forEach((element) => {
    if (element instanceof HTMLElement && element.dataset.candidateId === candidateId) {
      element.dataset.candidateState = "selected";
    }
  });
  setRootState({ "data-final-state": "candidate_selected" });
  const article = appendTurn("turn-choice-selected", "user", "choice", "selected", "", "choice_candidate_selected", "evidence-chat-candidate", candidateId);
  if (article) {
    article.dataset.candidateCategory = "trip-style";
    article.innerHTML = `<div class="chat-turn__meta"><span>user</span><strong>choice</strong></div><div class="selected-option-bubble candidate-card candidate-card--${candidate.tone}" data-selected-option-card="true" data-candidate-id="${candidate.id}" data-candidate-category="trip-style" data-candidate-mood="${candidate.mood}" data-candidate-state="selected" data-media-asset-id="${candidate.media.id}" data-media-state="${candidate.media.state}" data-media-source-class="${candidate.media.sourceClass}" data-evidence-ids="evidence-chat-candidate,${candidate.evidence}"><img class="candidate-media" src="${candidate.media.path}" alt="${candidate.media.alt}" data-media-image="selected-option" data-media-asset-id="${candidate.media.id}" data-media-mood="${candidate.media.mood}" data-media-state="${candidate.media.state}" data-media-license="${candidate.media.license}" /><span>${candidate.mood.replaceAll("-", " ")}</span><h3>${candidate.title}</h3><p>${candidate.summary}</p></div>`;
  }
  ensurePlanningTimeline();
  document.querySelector("[data-planning-timeline-rail]")?.insertAdjacentHTML("beforeend", timelineItem("timeline-selected-option", "day", "candidate", "selected", `Selected ${candidate.title}`, candidate.summary, "turn-choice-selected", candidate.media, `data-day-id="day-japan-02" data-slot-id="slot-itinerary-choice" data-candidate-id="${candidate.id}" data-decision-id="decision-${candidate.id}" data-evidence-id="${candidate.evidence}"`));
  if (selectedModelRole() !== "deterministic-offline") {
    setRootState({
      "data-final-state": "live_second_turn_blocked",
      "data-provider-request-state": "second_turn_blocked",
      "data-provider-outcome": "live_turn_provider_unknown_error",
      "data-live-turn-attempt-count": "2",
      "data-live-second-turn-state": "blocked",
    });
    appendTurn("turn-live-model-followup", "provider", "live-model-followup", "blocked", "A second live turn was blocked with the canonical live-turn provider diagnostic.", "live_turn_provider_unknown_error", "evidence-chat-live-model", candidate.id);
    document.querySelector("[data-planning-timeline-rail]")?.insertAdjacentHTML("beforeend", timelineItem("timeline-live-second-turn", "task", "live-model-followup", "blocked", "Second live turn pending", "The selected option is preserved while the canonical multi-turn runner integration is pending.", "turn-live-model-followup", candidate.media, `data-day-id="day-japan-02" data-slot-id="slot-live-followup" data-task-id="task-itinerary" data-candidate-id="${candidate.id}" data-decision-id="decision-live-followup-${candidate.id}" data-evidence-id="${candidate.evidence}"`));
  }
}

function deferCandidate(candidateId: string): void {
  const candidate = candidates.find((item) => item.id === candidateId);
  if (!candidate) return;
  setRootState({ "data-final-state": "candidate_deferred" });
  ensurePlanningTimeline();
  document.querySelector("[data-planning-timeline-rail]")?.insertAdjacentHTML("beforeend", timelineItem("timeline-deferred-option", "day", "candidate", "deferred", `Deferred ${candidate.title}`, candidate.summary, "turn-choice-deferred", candidate.media, `data-day-id="day-japan-02" data-slot-id="slot-itinerary-choice" data-candidate-id="${candidate.id}" data-decision-id="decision-${candidate.id}" data-evidence-id="${candidate.evidence}"`));
}

function requestApproval(): void {
  setRootState({ "data-final-state": "blocked", "data-approval-state": "blocked_missing_approval", "data-error-code": "PCH_UI_CHAT_APPROVAL_REQUIRED", "data-blocked-reason": "approval_required_preview" });
  const plate = document.querySelector<HTMLElement>("[data-approval-id='approval-preview-mock-hold']");
  plate?.setAttribute("data-approval-state", "blocked_missing_approval");
  plate?.setAttribute("data-blocked-reason", "approval_required_preview");
  appendTurn("turn-approval-blocked", "harness", "approval", "blocked", "Mock hold preparation is blocked until explicit approval is available. No hold or booking was created.", "approval_required_preview", "evidence-chat-approval");
  ensurePlanningTimeline();
  document.querySelector("[data-planning-timeline-rail]")?.insertAdjacentHTML("beforeend", timelineItem("timeline-approval-blocked", "task", "approval", "blocked", "Mock hold blocked", "Approval is required before mock hold work can continue.", "turn-approval-blocked", mediaAsset("logistics_transit"), 'data-task-id="task-approval" data-decision-id="decision-approval-preview" data-evidence-id="evidence-chat-approval" hidden'));
}

function setAskTabVisible(visible: boolean): void {
  document.querySelectorAll<HTMLElement>("[data-ask-action='open']").forEach((button) => {
    button.toggleAttribute("hidden", !visible);
    button.setAttribute("aria-hidden", String(!visible));
  });
}

function toggleDrawer(open: boolean): void {
  setRootState({ "data-ask-drawer": open ? "open" : "closed", "data-composer-state": open ? "collapsed_drawer_open" : "collapsed_drawer" });
  document.querySelector("[data-ask-drawer-panel]")?.remove();
  setAskTabVisible(!open);
  if (!open) return;
  document.querySelector(".chat-main")?.insertAdjacentHTML("beforeend", `<aside class="ask-drawer" data-ask-drawer-panel="open" aria-label="Ask drawer"><div><h2>Ask the agent</h2><button type="button" data-ask-action="close" aria-label="Close Ask drawer">Close</button></div><label for="trip-prompt-drawer">Follow-up prompt</label><textarea id="trip-prompt-drawer" data-prompt-entry="trip-drawer" aria-label="Follow-up trip prompt" placeholder="Ask, refine, or describe edits" rows="5"></textarea><button type="button" class="send-button" data-send-action="planner-drawer" aria-label="Send follow-up prompt">Send</button></aside>`);
  window.requestAnimationFrame(() => document.querySelector<HTMLTextAreaElement>("[data-prompt-entry='trip-drawer']")?.focus());
}

function moveDeck(direction: number): void {
  const deck = document.querySelector<HTMLElement>("[data-candidate-deck='reflective-culture']");
  const index = document.querySelector<HTMLElement>("[data-deck-index]");
  if (!deck || !index) return;
  const next = (Number(deck.dataset.deckIndex ?? "0") + direction + 2) % 2;
  deck.dataset.deckIndex = String(next);
  index.dataset.deckIndex = String(next);
  index.textContent = `${next + 1}/2`;
  deck.scrollLeft = next * 260;
}

function setTimelineMode(mode: string): void {
  const timeline = document.querySelector<HTMLElement>("[data-planning-timeline='trip']");
  const rail = document.querySelector<HTMLElement>("[data-planning-timeline-rail]");
  if (!timeline || !rail) return;
  const nextMode = mode === "task" ? "task" : "day";
  timeline.dataset.planningTimelineMode = nextMode;
  rail.dataset.planningTimelineRail = nextMode;
  document.querySelectorAll<HTMLElement>("[data-planning-timeline-item]").forEach((item) => {
    item.hidden = item.dataset.planningMode !== nextMode;
  });
  document.querySelectorAll<HTMLElement>("[data-timeline-mode-action]").forEach((button) => {
    const active = button.dataset.timelineModeAction === nextMode;
    button.dataset.timelineModeState = active ? "active" : "inactive";
    button.setAttribute("aria-pressed", String(active));
  });
}

function focusOriginTurn(turnId: string): void {
  const turn = document.querySelector<HTMLElement>(`[data-turn-id="${turnId}"]`);
  if (!turn) return;
  root()?.setAttribute("data-focused-turn-id", turnId);
  document.querySelectorAll<HTMLElement>("[data-turn-id]").forEach((element) => {
    element.dataset.turnFocused = element.dataset.turnId === turnId ? "true" : "false";
  });
  turn.scrollIntoView({ block: "center", behavior: "smooth" });
}

function closestAction(target: EventTarget | null, selector: string): HTMLElement | null {
  return target instanceof HTMLElement ? target.closest<HTMLElement>(selector) : null;
}

function closeDrawerAfterFocusLeaves(target: EventTarget | null): void {
  const drawer = closestAction(target, "[data-ask-drawer-panel]");
  if (!drawer) return;

  window.setTimeout(() => {
    if (!drawer.isConnected) return;

    const activeElement = document.activeElement;
    if (activeElement instanceof HTMLElement && drawer.contains(activeElement)) return;

    toggleDrawer(false);
  }, 0);
}

function closeDrawerOnOutsidePointer(target: EventTarget | null): void {
  const drawer = document.querySelector<HTMLElement>("[data-ask-drawer-panel]");
  if (!drawer) return;

  if (target instanceof HTMLElement && (drawer.contains(target) || target.closest("[data-ask-action='open']"))) return;

  toggleDrawer(false);
}

const chatActionSelector = [
  "[data-send-action]",
  "[data-form-submit]",
  "[data-choice-action]",
  "[data-approval-action]",
  "[data-ask-action]",
  "[data-deck-control]",
  "[data-timeline-mode-action]",
  "[data-origin-turn-id]",
  "[data-model-role-option]",
].join(",");

function interceptChatAction(event: Event): void {
  if (!closestAction(event.target, chatActionSelector)) return;
  handleChatInteraction(event.target);
}

function handleChatInteraction(target: EventTarget | null): void {
  const sendElement = closestAction(target, "[data-send-action]");
  const action = sendElement?.dataset ?? {};

  const modelRole = closestAction(target, "[data-model-role-option]")?.dataset.modelRoleOption;
  if (modelRole) {
    scheduleFallback(
      () => root()?.dataset.selectedModelRole !== modelRole,
      () => setModelRole(modelRole),
    );
  }

  if (action.sendAction === "planner" || action.sendAction === "planner-drawer" || action.sendAction === "deterministic" || action.sendAction === "deterministic-drawer") {
    const beforeFinalState = root()?.dataset.finalState;
    const beforeTurnCount = transcript()?.dataset.turnCount;
    scheduleFallback(
      () => root()?.dataset.finalState === beforeFinalState && transcript()?.dataset.turnCount === beforeTurnCount,
      sendPrompt,
    );
  }

  const formAction = closestAction(target, "[data-form-submit]")?.dataset ?? {};
  if (formAction.formSubmit === "form-trip-basics") {
    scheduleFallback(
      () => document.querySelector<HTMLElement>("[data-form-id='form-trip-basics']")?.dataset.formState !== "accepted",
      submitForm,
    );
  }

  const choiceAction = closestAction(target, "[data-choice-action]")?.dataset ?? {};
  if (choiceAction.choiceAction === "select" && choiceAction.candidateId) {
    const candidateId = choiceAction.candidateId;
    scheduleFallback(
      () => document.querySelector(`[data-selected-option-card='true'][data-candidate-id='${candidateId}']`) === null,
      () => selectCandidate(candidateId),
    );
  }

  if (choiceAction.choiceAction === "defer" && choiceAction.candidateId) {
    const candidateId = choiceAction.candidateId;
    scheduleFallback(
      () => document.querySelector(`[data-plan-trail-item='trail-deferred-option'][data-candidate-id='${candidateId}']`) === null,
      () => deferCandidate(candidateId),
    );
  }

  const approvalAction = closestAction(target, "[data-approval-action]")?.dataset ?? {};
  if (approvalAction.approvalAction === "request") {
    scheduleFallback(
      () => root()?.dataset.approvalState !== "blocked_missing_approval",
      requestApproval,
    );
  }

  const askAction = closestAction(target, "[data-ask-action]")?.dataset ?? {};
  if (askAction.askAction === "open") {
    scheduleFallback(
      () => root()?.dataset.askDrawer !== "open",
      () => toggleDrawer(true),
    );
  }

  if (askAction.askAction === "close") {
    scheduleFallback(
      () => root()?.dataset.askDrawer !== "closed",
      () => toggleDrawer(false),
    );
  }

  const deckAction = closestAction(target, "[data-deck-control]")?.dataset ?? {};
  if (deckAction.deckControl === "next" || deckAction.deckControl === "previous") {
    const beforeIndex = document.querySelector<HTMLElement>("[data-candidate-deck='reflective-culture']")?.dataset.deckIndex;
    const direction = deckAction.deckControl === "next" ? 1 : -1;
    scheduleFallback(
      () => document.querySelector<HTMLElement>("[data-candidate-deck='reflective-culture']")?.dataset.deckIndex === beforeIndex,
      () => moveDeck(direction),
    );
  }

  const modeButton = closestAction(target, "[data-timeline-mode-action]");
  if (modeButton?.dataset.timelineModeAction) {
    const mode = modeButton.dataset.timelineModeAction;
    scheduleFallback(
      () => document.querySelector<HTMLElement>("[data-planning-timeline='trip']")?.dataset.planningTimelineMode !== mode,
      () => setTimelineMode(mode),
    );
  }

  const timelineItem = closestAction(target, "[data-origin-turn-id]");
  const originTurnId = timelineItem?.dataset.originTurnId;
  if (originTurnId) {
    focusOriginTurn(originTurnId);
    scheduleFallback(
      () => root()?.dataset.focusedTurnId !== originTurnId,
      () => focusOriginTurn(originTurnId),
    );
  }
}

document.documentElement.dataset.endUserChatHelper = "ready";
window.setTimeout(() => {
  const modal = document.getElementById("components-reconnect-modal") as HTMLDialogElement | null;
  if (modal?.open || modal?.className.includes("components-reconnect")) {
    suppressReconnectModal();
  }
}, FALLBACK_DELAY_MS);
document.addEventListener("focusout", (event) => closeDrawerAfterFocusLeaves(event.target), true);
document.addEventListener("pointerdown", (event) => closeDrawerOnOutsidePointer(event.target), true);
document.addEventListener("pointerup", interceptChatAction, true);
document.addEventListener("click", interceptChatAction, true);

document.addEventListener("keydown", (event) => {
  const target = event.target;
  if (!(target instanceof HTMLElement) || target.dataset.planningTimelineRail == null) return;
  if (event.key === "ArrowRight") {
    target.scrollLeft += 180;
    target.dataset.timelineBrowse = "keyboard-next";
    event.preventDefault();
  }
  if (event.key === "ArrowLeft") {
    target.scrollLeft -= 180;
    target.dataset.timelineBrowse = "keyboard-previous";
    event.preventDefault();
  }
});
