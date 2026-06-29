const draftPrefix = "pch:form-draft:";
export function saveDraft(stageId, values) {
    const draft = {
        stageId,
        values,
        updatedAt: new Date().toISOString()
    };
    window.localStorage.setItem(`${draftPrefix}${stageId}`, JSON.stringify(draft));
}
export function readDraft(stageId) {
    const raw = window.localStorage.getItem(`${draftPrefix}${stageId}`);
    if (!raw)
        return null;
    try {
        return JSON.parse(raw);
    }
    catch {
        window.localStorage.removeItem(`${draftPrefix}${stageId}`);
        return null;
    }
}
export function clearDraft(stageId) {
    window.localStorage.removeItem(`${draftPrefix}${stageId}`);
}
export function focusFirstField(rootSelector) {
    const root = document.querySelector(rootSelector);
    const field = root?.querySelector("input:not([type=hidden]), select, textarea");
    if (!field)
        return false;
    field.focus();
    return true;
}
export function requiredFieldCount(rootSelector) {
    const root = document.querySelector(rootSelector);
    return root?.querySelectorAll("[data-required='true']").length ?? 0;
}
export function markRequiredSummary(rootSelector, summarySelector) {
    const requiredCount = requiredFieldCount(rootSelector);
    const summary = document.querySelector(summarySelector);
    if (!summary)
        return;
    const noun = requiredCount === 1 ? "field" : "fields";
    summary.dataset.requiredCount = requiredCount.toString();
    summary.textContent = `${requiredCount} required ${noun}`;
}
//# sourceMappingURL=forms.js.map