export const queryKeys = {
  contacts: (scopeId: string, filters: object) => [scopeId, 'contacts', filters] as const,
  contact: (scopeId: string, id: string) => [scopeId, 'contact', id] as const,
  tags: (scopeId: string) => [scopeId, 'tags'] as const,
  fields: (scopeId: string) => [scopeId, 'fields'] as const,
  contactTags: (scopeId: string, id: string) => [scopeId, 'contact-tags', id] as const,
  fieldValue: (scopeId: string, id: string, fieldId: string) => [scopeId, 'field-value', id, fieldId] as const,
  timeline: (scopeId: string, id: string) => [scopeId, 'timeline', id] as const,
};
