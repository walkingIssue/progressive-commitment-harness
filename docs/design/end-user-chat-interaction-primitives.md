# End-User Chat Interaction Primitives

Sprint 016 design references:

- Primary visual direction: `docs/design/assets/sprint-016-web-gpt-reference.png`
- `docs/design/assets/sprint-016-end-user-chat-concept.png`
- `docs/design/assets/sprint-016-agent-first-interaction-concept.png`

The end-user UI should feel like a live planning assistant, not an engineering fixture board. The harness remains typed and constrained, but the user should see a conversation that carries structured work objects inside it.

## Design Goals

- Make the primary screen usable without opening Stage Cockpit.
- Wire the primary flow to live model/provider reads when keys and credits are available.
- Keep deterministic/offline mode visible and usable as a fallback, not as the default illusion of completeness.
- Keep hold, book, pay, spend, and irreversible actions mocked and approval-gated.
- Preserve every option's `candidate_id` and evidence/claim provenance in stable `data-*` attributes.
- Separate interactive controls from decorative metadata so the page does not look like a wall of clickable cards.

## Layout

- **Slim navigation rail:** a narrow left rail with mode icons and user/session affordance. It should not compete with the task rail or chat.
- **Main agent canvas:** wide, calm center area where the agent's current work is the visual priority.
- **Transcript rhythm:** user bubbles are compact and right-aligned; assistant work bubbles are larger and left/center-aligned.
- **Assistant work area:** assistant turns can contain structured forms, choice cards, approval plates, evidence summaries, or blocked notices.
- **Right task rail:** a deep, high-contrast rail for decomposed tasks, progress, consensus, and blockers. Rows are collapsible and can light up green only when the user and model/harness have reached a stable accepted state.
- **Commitment/status strip:** shows commitment risk, deterministic/live mode, selected model roles, provider health, credit guard state, and last provider failure. It should be honest and compact.
- **Evidence/plan trail:** a browsable strip or rail of selected/planned cards that lets the user review what has been decided and why.
- **Stage Cockpit:** remains below or behind an engineering affordance, not the first thing an end user has to understand.

## Agent-First Interaction Rule

The start screen may center the user prompt because no task exists yet. After the first prompt is sent, the UI should shift visual priority to the agent's work.

Required behavior:

- The large text composer collapses after first submit.
- The collapsed composer becomes a compact side button or rail control, such as `Ask`.
- Pressing the collapsed control slides the composer out as a drawer.
- The transcript remains visible while the drawer is open.
- The agent work bubble stays visually dominant after the first turn.
- The task rail remains visible as the user's mental map of decomposed work.

The user should feel that the agent is actively planning and occasionally asking for guidance, not that the user is filling out a long form.

## Primary Visual Reference Notes

The `sprint-016-web-gpt-reference.png` screenshot is the closest current visual target. Match its product grammar, not its exact pixels.

Keep:

- spacious off-white planning canvas;
- narrow icon rail on the far left;
- dark navy task rail on the right;
- compact right-aligned user prompt bubbles;
- large assistant work bubble with a friendly lead sentence;
- strong heading hierarchy inside assistant work;
- destination/mood sections with illustrated or scenic backdrops;
- centered active option card with partial neighboring cards visible;
- selected state as a clear chip on the chosen card;
- selected option echoed back as a user bubble;
- a persistent evidence/plan trail from the generated concepts, adapted to the visual style here;
- low-commitment/commitment-risk chip near the top;
- floating `Ask` button after the main prompt has collapsed;
- task rail progress lines, status pills, and `Ask` actions for future steps;
- reassuring control copy such as "You're in control. We'll only move forward when you're ready."

Avoid:

- generic Bootstrap card stacks;
- oversized always-visible prompt textarea after the first turn;
- making every metadata element look clickable;
- dark task rail consuming the whole page;
- raw debug labels in the end-user surface.

## Primitives

### Composer

Purpose: collect the user's free-text planning prompt or correction.

Required states:

- expanded_start
- collapsed_drawer
- idle
- sending
- model_running
- blocked_provider
- blocked_harness
- awaiting_user_input

Required controls:

- text input
- send button
- collapsed `Ask` button after first turn
- slide-out drawer open/close
- deterministic/live mode indicator
- optional model role picker
- optional attachment/search toggle later

### Assistant Work Bubble

Purpose: wrap model/harness output in a conversational lead sentence plus a typed work object.

Rules:

- The lead sentence can be warm and human.
- The work object must be typed: form, choices, approval, summary, evidence, or blocked notice.
- Unsupported new facts are not allowed in the lead sentence.
- Raw provider payloads and raw user prompt text are never rendered.

### Form Card

Purpose: collect structured user input requested by the harness or model-facing packet.

Required data:

- form id
- field ids
- current field values
- required/optional markers
- evidence ids when a field is prefilled or inferred

States:

- draft
- submitted
- accepted
- validation_blocked
- pending_confirmation

### Choice Set Card

Purpose: collapse candidate pools into a small number of choices.

Required data:

- choice set id
- candidate id per option
- candidate kind/category
- evidence ids
- source/provenance

Controls:

- select
- defer
- compare/details
- ask for more

Selection echo:

- When the user selects an option, the selected option card is copied into the transcript as a compact user interaction bubble.
- Do not replace the selected card with plain text like "I choose X".
- The echoed bubble should render a card receipt: thumbnail/backdrop or mood swatch, title, category/mood, key safe facts, and selected state.
- The echoed card must include trusted ids in `data-*` attributes, not raw provider payloads.
- The original option remains in the assistant work card with selected state.

### Candidate Option Card

Purpose: visually represent a concrete candidate, such as a meal, lodging, activity, transit, or downtime option.

Rules:

- It may include an image or image placeholder.
- It must keep `candidate_id` visible to the DOM through `data-candidate-id`.
- Display names must come from trusted provider/candidate data or sanitized model output.
- It should make selected, deferred, unavailable, and needs-review states visually distinct.

Mood and feel treatment:

- Candidate cards may use colored scenic or abstract backdrops to communicate feel, such as calm morning, lively food, reflective culture, soft nature, focused logistics, or restorative downtime.
- Mood color is a visual hint, not a source of truth; it must not imply unsupported facts.
- Cards in the same mood can be grouped as a floaty stacked deck.
- The stacked deck should show partial card overlap and horizontal scroll/swipe controls.
- The active card must remain keyboard reachable and screen-reader identifiable.
- Each card in a deck must preserve its own candidate id and evidence ids.

### Approval Plate

Purpose: make commitment-like actions feel materially different from ordinary choices.

Used for:

- quote requiring spend review
- mock hold preparation
- booking/payment/spend-like actions
- irreversible or high-risk changes

Visual treatment:

- heavier border
- metallic or high-weight surface
- clear warning/approval language
- no hidden approval tokens

### Task Rail

Purpose: show decomposed planning tasks without overwhelming the conversation.

Task states:

- not_started
- active
- model_running
- needs_user
- accepted
- blocked
- deferred

Rules:

- Green only means accepted or fulfilled.
- Amber means pending user/model/harness confirmation.
- Red means blocked.
- Grey means deferred/not started.
- Metadata rows should not look like buttons unless expandable.

Visual direction:

- Use a deep navy/marine background.
- Active and completed groups can sit in raised translucent rows.
- Progress substeps use a vertical line with small status nodes.
- Status pills should be compact: ready, in progress, ask, blocked.
- A bottom reassurance panel can remind the user that no forward move happens without confirmation.

### Evidence And Claim Strip

Purpose: show why a recommendation exists without turning provenance into clutter.

Rules:

- Plain typography and dividers, not elevated cards.
- Bounded evidence refs.
- No raw prompt/provider payloads.
- Claim provenance remains available in `data-*` attributes and detail drawers.

### Evidence And Plan Trail

Purpose: give the user a visually browsable history of what has been planned, selected, deferred, or blocked.

This should feel like a living itinerary memory, not a debug trace.

Required contents:

- selected option cards;
- accepted mission facts;
- pending confirmations;
- availability/quote preview states;
- mock hold/approval states;
- evidence ids and claim provenance through stable `data-*` attributes;
- sanitized "why this is here" text.

Interaction:

- horizontally scrollable or carousel-like;
- can sit below the assistant work card, between major transcript turns, or as a compact strip in the main canvas;
- selected cards can be revisited to open a detail drawer;
- each item should show whether it came from user-stated data, trusted harness state, provider data, or model inference pending confirmation.

Rules:

- Do not render raw prompts, provider payloads, approval tokens, secrets, or exception text.
- Do not make the trail look like the active task rail; it is memory/evidence, not the current to-do list.
- Use softer surfaces than active choice cards, but keep it visibly valuable.

### Provider Failure Notice

Purpose: make live model/provider failure understandable and recoverable.

Fixed states:

- key_missing
- credit_exhausted
- provider_timeout
- empty_content
- malformed_schema
- packet_mismatch
- intake_blocked

Controls:

- retry same role/model
- switch model role
- continue deterministic
- ask user for missing confirmation

## Theme Direction

- Replace the flat Bootstrap/green default with a warm, high-contrast product UI.
- Light theme: soft paper/off-white base, deep ink text, teal active accents, amber pending, coral/rose blocked or approval states.
- Dark theme: deep marine base, raised marine surfaces, desaturated pastel accents tuned for contrast.
- Avoid decorative orbs and generic marketing hero patterns.
- Use elevated cards only for interactive work objects, modals, and repeated candidate options.
- Use plain text bands/dividers for status metadata.

## Browser Acceptance Gate

The UI is not READY unless browser automation proves:

- the prompt field accepts text;
- send changes the transcript and final state;
- at least one form card submits;
- at least one choice card selects or defers;
- at least one approval plate blocks without approval;
- deterministic fallback is visible when live providers are disabled;
- live mode visibly calls a provider when configured;
- raw prompt/provider/approval/secret sentinels are absent from rendered DOM text.
