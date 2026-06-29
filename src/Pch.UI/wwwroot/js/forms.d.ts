export type DraftValue = string | boolean | string[];
export interface FormDraft {
    stageId: string;
    values: Record<string, DraftValue>;
    updatedAt: string;
}
export declare function saveDraft(stageId: string, values: Record<string, DraftValue>): void;
export declare function readDraft(stageId: string): FormDraft | null;
export declare function clearDraft(stageId: string): void;
export declare function focusFirstField(rootSelector: string): boolean;
export declare function requiredFieldCount(rootSelector: string): number;
