import { useState } from 'react';
import type { Pipeline, PipelineStage } from '../api';

const ICONS: Record<string, string> = {
  caption: '📷',
  search: '🔎',
  answer: '✍️',
};

const LABELS: Record<string, string> = {
  caption: 'Read image',
  search: 'Vector search',
  answer: 'Compose answer',
};

function fmtMs(ms: number) {
  if (ms < 1000) return `${ms} ms`;
  return `${(ms / 1000).toFixed(2)} s`;
}

export function PipelinePanel({ pipeline }: { pipeline: Pipeline }) {
  const [open, setOpen] = useState(false);

  const failed = pipeline.stages.some(s => !s.ok);
  const ranStages = pipeline.stages.filter(s => !(s.data?.skipped));

  return (
    <div className="mt-3 pt-3 border-t border-zinc-800/80">
      <button
        onClick={() => setOpen(o => !o)}
        className="w-full flex items-center gap-2 text-[10px] uppercase tracking-wider text-zinc-500 hover:text-zinc-300"
      >
        <span>{open ? '▾' : '▸'}</span>
        <span>Pipeline</span>
        <span className="text-zinc-600 normal-case tracking-normal">
          · {ranStages.length} step{ranStages.length === 1 ? '' : 's'} · {fmtMs(pipeline.totalMs)}
        </span>
        {failed && <span className="text-red-400 normal-case tracking-normal">· error</span>}
      </button>

      {open && (
        <ol className="mt-3 space-y-2 fade-in">
          {pipeline.stages.map((s, i) => (
            <StageRow key={i} stage={s} />
          ))}
        </ol>
      )}
    </div>
  );
}

function StageRow({ stage }: { stage: PipelineStage }) {
  const [expanded, setExpanded] = useState(false);
  const skipped = stage.data?.skipped === true;
  const icon = ICONS[stage.stage] ?? '·';
  const label = LABELS[stage.stage] ?? stage.stage;

  const statusClass = !stage.ok
    ? 'text-red-300 border-red-700/60 bg-red-950/30'
    : skipped
      ? 'text-zinc-500 border-zinc-800 bg-zinc-900/30'
      : 'text-zinc-200 border-zinc-800 bg-zinc-900/50';

  const dot = !stage.ok ? '✕' : skipped ? '–' : '✓';
  const dotColor = !stage.ok ? 'text-red-400' : skipped ? 'text-zinc-600' : 'text-emerald-400';

  return (
    <li className={`rounded-md border ${statusClass}`}>
      <button
        onClick={() => setExpanded(e => !e)}
        className="w-full flex items-center gap-2 px-3 py-2 text-left"
      >
        <span className="text-base leading-none">{icon}</span>
        <span className="text-sm flex-1">{label}</span>
        <span className={`text-xs font-mono ${dotColor}`}>{dot}</span>
        <span className="text-[11px] text-zinc-500 tabular-nums w-14 text-right">
          {skipped ? 'skipped' : fmtMs(stage.ms)}
        </span>
      </button>

      {expanded && (
        <div className="px-3 pb-3 fade-in">
          <StageDetails stage={stage} />
        </div>
      )}
    </li>
  );
}

function StageDetails({ stage }: { stage: PipelineStage }) {
  if (stage.error) {
    return <div className="text-xs text-red-300 font-mono">{stage.error}</div>;
  }
  if (stage.data?.skipped) {
    return <div className="text-xs text-zinc-500 italic">Skipped — {stage.data.reason}</div>;
  }

  switch (stage.stage) {
    case 'caption':
      return <CaptionDetails data={stage.data} />;
    case 'search':
      return <SearchDetails data={stage.data} />;
    case 'answer':
      return <AnswerDetails data={stage.data} />;
    default:
      return <pre className="text-xs text-zinc-400 whitespace-pre-wrap">{JSON.stringify(stage.data, null, 2)}</pre>;
  }
}

function CaptionDetails({ data }: { data: any }) {
  return (
    <div className="space-y-2 text-xs">
      <div className="text-zinc-500">
        {data?.imageCount ?? 0} image{data?.imageCount === 1 ? '' : 's'} processed
      </div>
      <div className="rounded bg-zinc-950 border border-zinc-800 p-2">
        <div className="text-[10px] uppercase tracking-wider text-zinc-500 mb-1">Extracted keywords</div>
        <div className="text-zinc-200 whitespace-pre-wrap font-mono leading-snug">{data?.caption}</div>
      </div>
    </div>
  );
}

function SearchDetails({ data }: { data: any }) {
  const results = (data?.results ?? []) as Array<{
    documentId: string;
    title?: string | null;
    url?: string | null;
    relevance: number;
    snippet: string;
  }>;
  return (
    <div className="space-y-2 text-xs">
      <div className="rounded bg-zinc-950 border border-zinc-800 p-2">
        <div className="text-[10px] uppercase tracking-wider text-zinc-500 mb-1">
          Embedding query · {data?.queryLength} chars{data?.filter ? ` · filter: ${data.filter}` : ''}
        </div>
        <div className="text-zinc-200 font-mono leading-snug break-words">{data?.query}</div>
      </div>

      {results.length === 0 ? (
        <div className="text-zinc-500 italic">No results matched.</div>
      ) : (
        <div className="space-y-1.5">
          <div className="text-[10px] uppercase tracking-wider text-zinc-500">
            Retrieved {results.length} chunk{results.length === 1 ? '' : 's'} (limit {data?.limit})
          </div>
          {results.map((r, i) => (
            <div key={i} className="rounded border border-zinc-800 bg-zinc-950 p-2 space-y-1">
              <div className="flex items-center gap-2">
                <span className="text-zinc-500 tabular-nums w-10 shrink-0">{r.relevance.toFixed(2)}</span>
                <div className="flex-1 h-1 bg-zinc-800 rounded overflow-hidden">
                  <div
                    className="h-full bg-gradient-to-r from-purple-500 to-fuchsia-400"
                    style={{ width: `${Math.min(100, r.relevance * 100)}%` }}
                  />
                </div>
              </div>
              <div>
                {r.url ? (
                  <a href={r.url} target="_blank" rel="noreferrer" className="text-purple-400 hover:text-purple-300 truncate block">
                    {r.title || r.documentId}
                  </a>
                ) : (
                  <span className="text-zinc-200">{r.title || r.documentId}</span>
                )}
              </div>
              <div className="text-zinc-500 leading-snug">{r.snippet}</div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

function AnswerDetails({ data }: { data: any }) {
  return (
    <div className="grid grid-cols-2 gap-2 text-xs">
      <Stat label="Citations used" value={data?.citationsUsed} />
      <Stat label="Images sent" value={data?.imagesSent} />
      <Stat label="Image text in prompt" value={data?.imageTextInPrompt ? 'yes' : 'no'} />
      <Stat label="History turns" value={data?.historyTurns ?? 0} />
      <Stat label="Prompt length" value={`${data?.promptLength ?? 0} chars`} />
      <Stat label="Answer length" value={`${data?.answerLength ?? 0} chars`} />
      <Stat label="Model" value={data?.model} />
    </div>
  );
}

function Stat({ label, value }: { label: string; value: any }) {
  return (
    <div className="rounded bg-zinc-950 border border-zinc-800 p-2">
      <div className="text-[10px] uppercase tracking-wider text-zinc-500">{label}</div>
      <div className="text-zinc-200 font-mono">{String(value ?? '—')}</div>
    </div>
  );
}
