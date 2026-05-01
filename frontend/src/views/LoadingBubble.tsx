import { useEffect, useState } from 'react';

const TEXT_STAGES = ['Searching knowledge base', 'Composing answer'];
const IMAGE_STAGES = ['Reading image', 'Searching knowledge base', 'Composing answer'];

export function LoadingBubble({ hasImages }: { hasImages: boolean }) {
  const stages = hasImages ? IMAGE_STAGES : TEXT_STAGES;
  const [stageIdx, setStageIdx] = useState(0);

  useEffect(() => {
    if (stageIdx >= stages.length - 1) return;
    // First stage (image read) takes longer; rotate at 2.2s, others at 1.6s
    const ms = stageIdx === 0 && hasImages ? 2200 : 1600;
    const t = setTimeout(() => setStageIdx(i => Math.min(i + 1, stages.length - 1)), ms);
    return () => clearTimeout(t);
  }, [stageIdx, stages.length, hasImages]);

  return (
    <div className="flex justify-start fade-in">
      <div className="bg-zinc-900 border border-zinc-800 rounded-2xl px-4 py-3 flex items-center gap-3 min-w-[200px]">
        <span className="inline-flex pulse-purple rounded-full">
          <span className="typing-dot" />
          <span className="typing-dot" />
          <span className="typing-dot" />
        </span>
        <span className="text-sm text-zinc-400 fade-in" key={stageIdx}>
          {stages[stageIdx]}…
        </span>
      </div>
    </div>
  );
}
