# End-User Chat Interaction Primitives

Sprint 016 design reference: `docs/design/assets/sprint-016-end-user-chat-concept.png`.

The end-user UI should feel like a live planning assistant, not an engineering fixture board. The harness remains typed and constrained, but the user should see a conversation that carries structured work objects inside it.

## Design Goals

- Make the primary screen usable without opening Stage Cockpit.
- Wire the primary flow to live model/provider reads when keys and credits are available.
- Keep deterministic/offline mode visible and usable as a fallback, not as the default illusion of completeness.
- Keep hold, book, pay, spend, and irreversible actions mocked and approval-gated.
- Preserve every option's `candidate_id` and evidence/claim provenance in stable `data-*` attributes.
- Separate interactive controls from decorative metadata so the page does not look like a wall of clickable cards.

## Layout

- **Main conversation column:** transcript turns flow top-to-bottom; the active composer is pinned near the bottom.
- **Assistant work area:** assistant turns can contain structured forms, choice cards, approval plates, evidence summaries, or blocked notices.
- **Right task rail:** decomposed tasks show status, progress, consensus, and blockers. Rows are collapsible and can light up green only when the user and model/harness have reached a stable accepted state.
- **Model/status strip:** shows deterministic/live mode, selected model roles, provider health, credit guard state, and last provider failure. It should be honest and compact.
- **Stage Cockpit:** remains below or behind an engineering affordance, not the first thing an end user has to understand.

## Primitives

### Composer

Purpose: collect the user's free-text planning prompt or correction.

Required states:

- idle
- sending
- model_running
- blocked_provider
- blocked_harness
- awaiting_user_input

Required controls:

- text input
- send button
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

### Candidate Option Card

Purpose: visually represent a concrete candidate, such as a meal, lodging, activity, transit, or downtime option.

Rules:

- It may include an image or image placeholder.
- It must keep `candidate_id` visible to the DOM through `data-candidate-id`.
- Display names must come from trusted provider/candidate data or sanitized model output.
- It should make selected, deferred, unavailable, and needs-review states visually distinct.

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

### Evidence And Claim Strip

Purpose: show why a recommendation exists without turning provenance into clutter.

Rules:

- Plain typography and dividers, not elevated cards.
- Bounded evidence refs.
- No raw prompt/provider payloads.
- Claim provenance remains available in `data-*` attributes and detail drawers.

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
