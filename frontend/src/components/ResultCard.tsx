import { useState } from 'react';
import type { SearchResult } from '../api';
import { RelevanceBar } from './RelevanceBar';

function tagFirst(tags: Record<string, (string | null)[]>, key: string) {
  const v = tags[key];
  return v && v.length > 0 ? v[0] ?? undefined : undefined;
}

export function ResultCard({ result, onDelete }: { result: SearchResult; onDelete?: (id: string) => void }) {
  const top = result.partitions[0];
  const url = top ? tagFirst(top.tags, 'url') : undefined;
  const title = top ? tagFirst(top.tags, 'title') : undefined;
  const source = top ? tagFirst(top.tags, 'source') : undefined;

  return (
    <div className="rounded-lg border border-zinc-800 bg-zinc-900/50 p-4 space-y-3">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <div className="flex items-center gap-2 flex-wrap">
            {title ? (
              url ? (
                <a href={url} target="_blank" rel="noreferrer" className="text-purple-300 hover:text-purple-200 font-medium truncate">
                  {title}
                </a>
              ) : (
                <span className="text-purple-300 font-medium truncate">{title}</span>
              )
            ) : (
              <span className="text-zinc-200 font-medium truncate">{result.documentId}</span>
            )}
            {source && (
              <span className="text-[10px] uppercase tracking-wider px-1.5 py-0.5 rounded bg-zinc-800 text-zinc-400">
                {source}
              </span>
            )}
          </div>
          <div className="text-xs text-zinc-500 mt-0.5 truncate">
            {result.documentId} · {result.sourceName}
          </div>
        </div>
        {onDelete && (
          <button
            onClick={() => onDelete(result.documentId)}
            className="text-xs px-2 py-1 rounded border border-zinc-700 text-zinc-400 hover:text-red-300 hover:border-red-500/50"
            title="Delete document"
          >
            Delete
          </button>
        )}
      </div>

      <div className="space-y-2">
        {result.partitions.map((p, i) => (
          <Partition key={i} text={p.text} relevance={p.relevance} />
        ))}
      </div>
    </div>
  );
}

function Partition({ text, relevance }: { text: string; relevance: number }) {
  const [expanded, setExpanded] = useState(false);
  const long = text.length > 400;
  const shown = expanded || !long ? text : text.slice(0, 400) + '…';
  return (
    <div className="space-y-1.5">
      <RelevanceBar value={relevance} />
      <pre className="whitespace-pre-wrap text-sm text-zinc-300 font-sans">{shown}</pre>
      {long && (
        <button onClick={() => setExpanded(e => !e)} className="text-xs text-purple-400 hover:text-purple-300">
          {expanded ? 'Show less' : 'Show more'}
        </button>
      )}
    </div>
  );
}
