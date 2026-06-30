# End-User Progressive History And Editing

Sprint 018 should make the planning history feel like part of the product, not part of the chat transcript.

The current chat-first trail proves the evidence idea, but the better product shape is a separate planning timeline below or adjacent to the main interaction area. The assistant remains the place where new work happens; the trail becomes the user's living itinerary memory.

## Goals

- Keep the agent's current work front and center.
- Move selected, deferred, blocked, and approved cards out of the chat transcript into a dedicated evidence/planning element.
- Let the user revisit planned decisions without scrolling through raw transcript text.
- Prepare the harness for editing already-decided nodes and identifying the smallest affected repair set.
- Keep Stage Cockpit available for engineering, but do not make it share the end-user route.

## Primary UI Changes

### Separate Product Route

Create a distinct end-user route for the product chat, separate from Stage Cockpit.

- `/` or `/trip` should load the end-user trip planner.
- `/stage-cockpit` should load the engineering cockpit.
- The end-user route should not render Stage Cockpit below the chat.
- The cockpit can remain linked from a small engineering affordance.

### Folded Composer

After the first user prompt, the composer should fold into the right side of the chat view rather than float outside the layout.

Required behavior:

- The start screen keeps the centered full prompt.
- After first send, the input folds into a quiet right-edge button inside the chat area.
- The folded button keeps roughly the original textbox height but narrows to about the inset width of a user bubble.
- The folded button uses low-contrast pastel motion so it is findable without competing with the assistant work card.
- Pressing it slides the composer out as an in-context drawer.

Visual direction:

- Use a pale sea-glass or soft teal background pulse.
- Keep details subtle: thin border, small sparkle/send icon, no heavy call-to-action color.
- It should feel available, not needy.

### Planning Timeline

Move evidence/decision trace into a separate timeline/carousel element outside the main chat transcript.

Modes:

- **Day mode:** one lane per planned day. As the user selects activities, meals, transit, holds, and availability previews, the day fills with cards.
- **Task mode:** one lane per decomposed strong-model task. Each task can contain days or clusters of decisions so the user can zoom from "plan Japan trip" to "day 3 food/activity choices."

Interactions:

- Horizontal carousel or rail with visible selected/deferred/blocked states.
- Clicking an item scrolls the main interaction window to the originating assistant/user turn.
- Clicking an item can open a detail/edit drawer.
- Items keep trusted ids: task id, day id, slot id, candidate id, decision id, evidence ids.
- Items are visually softer than active cards, but still useful and image-backed.

### Component Boundaries

Break the end-user UI into Blazor components before it grows into an unmaintainable surface.

Recommended components:

- `EndUserChatPage`
- `ChatShell`
- `ComposerDrawer`
- `AssistantWorkBubble`
- `ChoiceDeck`
- `OptionCard`
- `SelectedOptionBubble`
- `PlanningTimeline`
- `PlanningTimelineItem`
- `TaskRail`
- `EvidencePillStrip`
- `ApprovalPlate`
- `ProviderStatusStrip`

CSS should follow the same boundary: page shell, chat primitives, card/deck primitives, timeline primitives, task rail, and responsive rules.

## Harness Editing Problem

Editing a past decision is not only a UI affordance. Once a selected node changes, later nodes may depend on it.

The harness needs a small dependency graph for planning decisions:

- mission facts;
- pending confirmations;
- itinerary days;
- slots;
- candidates;
- selected/deferred decisions;
- availability/quote previews;
- mock holds;
- evidence/export snapshots.

For an edit, the harness should answer:

- Which decided nodes are directly invalidated?
- Which downstream nodes are possibly affected?
- Which unaffected nodes can be preserved?
- What is the smallest user-facing repair prompt?
- Can compatible nodes be merged automatically without asking the user?

Suggested result shape:

- `EditImpactRequest`: session id, edited node id, proposed change, observed revision/fingerprint.
- `EditImpactResult`: accepted/blocked, fixed code, affected nodes, preserved nodes, minimal repair prompts, merge suggestions.
- `PlanningNode`: node id, kind, status, depends-on ids, evidence ids, user-visible label code.

Blocked or unsafe edit paths must not echo raw prompt text, provider payloads, approval tokens, credentials, or exception text.

## Sprint 018 Priorities

Highest user value:

1. Split the end-user planner from Stage Cockpit.
2. Move the planning/evidence trail into a separate image-backed timeline with day/task modes.
3. Make timeline items click-to-scroll to the originating interaction.
4. Fold the composer into a quiet in-chat right-edge button after first send.
5. Componentize the Blazor UI so future interaction primitives do not become one giant file.

Foundational harness value:

1. Add a deterministic planning decision graph snapshot.
2. Add edit-impact detection for changed selected candidates or changed day slots.
3. Return fixed sanitized impacted/preserved node summaries for UI display.

Deferred:

- Full automatic merge resolution.
- Multi-user edits.
- Durable database-backed revision history.
- Live provider re-query after edits.
