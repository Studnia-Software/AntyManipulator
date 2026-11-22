export function getVerdict(verdict: never) {
  return (
    verdictConfig[verdict] || {
      label: verdict || 'Nieznany werdykt',
      color: 'gray',
      icon: 'i',
    }
  );
}

export function formatDate(date: never) {
  if (!date) return 'Brak daty';

  const parsed = new Date(date);

  if (Number.isNaN(parsed.getTime())) return 'Brak daty';

  return new Intl.DateTimeFormat('pl-PL', {
    day: '2-digit',
    month: 'long',
    year: 'numeric',
  }).format(parsed);
}

export function getRiskColor(level: never) {
  if (level === 'High') return 'red';
  if (level === 'Medium') return 'yellow';
  if (level === 'Low') return 'green';
  return 'gray';
}

const verdictConfig = {
  Confirmed: {
    label: 'Potwierdzone',
    color: 'green',
    icon: '✓',
  },
  Refuted: {
    label: 'Obalone',
    color: 'red',
    icon: '✕',
  },
  PartiallyConfirmed: {
    label: 'Częściowo potwierdzone',
    color: 'yellow',
    icon: '◐',
  },
  InsufficientEvidence: {
    label: 'Brak wystarczających dowodów',
    color: 'amber',
    icon: '?',
  },
  Misleading: {
    label: 'Wprowadzające w błąd',
    color: 'orange',
    icon: '!',
  },
};

export const colorClasses = {
  green: {
    badge: 'bg-green-50 text-green-700 border-green-200',
    panel: 'bg-green-50/50 border-green-200',
    icon: 'bg-green-100 text-green-700',
  },
  red: {
    badge: 'bg-red-50 text-red-700 border-red-200',
    panel: 'bg-red-50/50 border-red-200',
    icon: 'bg-red-100 text-red-700',
  },
  yellow: {
    badge: 'bg-yellow-50 text-yellow-800 border-yellow-200',
    panel: 'bg-yellow-50/50 border-yellow-200',
    icon: 'bg-yellow-100 text-yellow-800',
  },
  amber: {
    badge: 'bg-amber-50 text-amber-700 border-amber-200',
    panel: 'bg-amber-50/50 border-amber-200',
    icon: 'bg-amber-100 text-amber-700',
  },
  orange: {
    badge: 'bg-orange-50 text-orange-700 border-orange-200',
    panel: 'bg-orange-50/50 border-orange-200',
    icon: 'bg-orange-100 text-orange-700',
  },
  gray: {
    badge: 'bg-gray-100 text-gray-600 border-gray-200',
    panel: 'bg-gray-50 border-gray-200',
    icon: 'bg-gray-100 text-gray-600',
  },
};
