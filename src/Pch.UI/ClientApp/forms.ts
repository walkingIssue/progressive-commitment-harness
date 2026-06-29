export type DraftValue = string | boolean | string[] | null;

export interface FormDraft {
  stageId: string;
  values: Record<string, DraftValue>;
  updatedAt: string;
}

const draftPrefix = "pch:form-draft:";

export function saveDraft(stageId: string, values: Record<string, DraftValue>): void {
  const draft: FormDraft = {
    stageId,
    values,
    updatedAt: new Date().toISOString()
  };
  window.localStorage.setItem(`${draftPrefix}${stageId}`, JSON.stringify(draft));
}

export function readDraft(stageId: string): FormDraft | null {
  const raw = window.localStorage.getItem(`${draftPrefix}${stageId}`);
  if (!raw) return null;

  try {
    return JSON.parse(raw) as FormDraft;
  } catch {
    window.localStorage.removeItem(`${draftPrefix}${stageId}`);
    return null;
  }
}

export function clearDraft(stageId: string): void {
  window.localStorage.removeItem(`${draftPrefix}${stageId}`);
}

export function focusFirstField(rootSelector: string): boolean {
  const root = document.querySelector(rootSelector);
  const field = root?.querySelector<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>(
    "input:not([type=hidden]), select, textarea"
  );

  if (!field) return false;
  field.focus();
  return true;
}

export function requiredFieldCount(rootSelector: string): number {
  const root = document.querySelector(rootSelector);
  return root?.querySelectorAll("[data-required='true']").length ?? 0;
}

export function markRequiredSummary(rootSelector: string, summarySelector: string): void {
  const requiredCount = requiredFieldCount(rootSelector);
  const summary = document.querySelector<HTMLElement>(summarySelector);
  if (!summary) return;

  const noun = requiredCount === 1 ? "field" : "fields";
  summary.dataset.requiredCount = requiredCount.toString();
  summary.textContent = `${requiredCount} required ${noun}`;
}
