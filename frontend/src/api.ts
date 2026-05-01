const BASE = '/api/rag';

export type SearchPartition = {
  text: string;
  relevance: number;
  partitionNumber: number;
  sectionNumber: number;
  lastUpdate: string;
  tags: Record<string, (string | null)[]>;
};

export type SearchResult = {
  link: string;
  index: string;
  documentId: string;
  fileId: string;
  sourceContentType: string;
  sourceName: string;
  sourceUrl: string;
  partitions: SearchPartition[];
};

export type SearchResponse = {
  query: string;
  noResult: boolean;
  results: SearchResult[];
};

export type AskSource = {
  documentId: string;
  title: string | null;
  url: string | null;
  sourceName: string;
  link: string;
  relevance: number;
};

export type PipelineStage = {
  stage: 'caption' | 'search' | 'answer' | string;
  ok: boolean;
  ms: number;
  error?: string | null;
  data?: any;
};

export type Pipeline = {
  stages: PipelineStage[];
  totalMs: number;
};

export type AskResponse = {
  answer: string;
  sources: AskSource[];
  imageCaption?: string | null;
  searchQuery?: string | null;
  pipeline?: Pipeline | null;
};

export type AskHistoryItem = { role: 'user' | 'assistant'; content: string };

async function http<T>(path: string, init?: RequestInit): Promise<T> {
  const r = await fetch(`${BASE}${path}`, {
    ...init,
    headers: { 'Content-Type': 'application/json', ...(init?.headers || {}) },
  });
  if (!r.ok) {
    const text = await r.text().catch(() => '');
    throw new Error(`${r.status} ${r.statusText}${text ? ` — ${text}` : ''}`);
  }
  if (r.status === 204) return undefined as T;
  return r.json() as Promise<T>;
}

export const api = {
  search: (q: string, limit = 5) =>
    http<SearchResponse>(`/search?q=${encodeURIComponent(q)}&limit=${limit}`),

  ask: (question: string, filter?: string, history?: AskHistoryItem[]) =>
    http<AskResponse>('/ask', {
      method: 'POST',
      body: JSON.stringify({ question, filter: filter || null, history: history ?? null }),
    }),

  askWithImages: async (question: string, filter: string | undefined, images: Blob[], history?: AskHistoryItem[]): Promise<AskResponse> => {
    const fd = new FormData();
    fd.append('question', question);
    if (filter) fd.append('filter', filter);
    if (history && history.length) fd.append('history', JSON.stringify(history));
    images.forEach((b, i) => fd.append('images', b, `image-${i}.${(b.type.split('/')[1] || 'png').replace('+xml','')}`));
    const r = await fetch(`${BASE}/ask-multipart`, { method: 'POST', body: fd });
    if (!r.ok) {
      const text = await r.text().catch(() => '');
      throw new Error(`${r.status} ${r.statusText}${text ? ` — ${text}` : ''}`);
    }
    return r.json();
  },

  ingestText: (id: string, text: string, tags?: Record<string, string>) =>
    http<{ documentId: string }>('/ingest-text', {
      method: 'POST',
      body: JSON.stringify({ id, text, tags: tags ?? null }),
    }),

  ingestOrigamTopic: (topicIdOrUrl: string) =>
    http<{ docId: string; title: string; url: string; posts: number; chars: number }>(
      '/ingest-origam-topic',
      { method: 'POST', body: JSON.stringify({ topicIdOrUrl }) },
    ),

  deleteDocument: (id: string) =>
    http<void>(`/document/${encodeURIComponent(id)}`, { method: 'DELETE' }),
};
