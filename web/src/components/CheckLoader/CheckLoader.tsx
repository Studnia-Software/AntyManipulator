import { useEffect, useState } from 'react';

// Kolejne etapy pipeline'u w API. API nie raportuje postępu, więc komunikaty
// zmieniają się co kilka sekund, żeby pokazać, że sprawdzanie trwa.
const STEPS = [
  'Pobieramy i czytamy materiał…',
  'Wyodrębniamy twierdzenia do sprawdzenia…',
  'Przeszukujemy serwisy fact-checkingowe i inne źródła…',
  'Porównujemy źródła i sprawdzamy cytaty…',
  'Przygotowujemy wynik…',
];

const STEP_MS = 6000;

export const CheckLoader = () => {
  const [step, setStep] = useState(0);
  const [seconds, setSeconds] = useState(0);

  useEffect(() => {
    const stepTimer = setInterval(
      () => setStep((s) => Math.min(s + 1, STEPS.length - 1)),
      STEP_MS,
    );
    const clock = setInterval(() => setSeconds((s) => s + 1), 1000);
    return () => {
      clearInterval(stepTimer);
      clearInterval(clock);
    };
  }, []);

  return (
    <div
      className="min-h-screen flex flex-col items-center justify-center px-5 text-center"
      role="status"
      aria-live="polite"
    >
      <div className="relative w-16 h-16 mb-8">
        <div className="absolute inset-0 rounded-full border-4 border-blue-100" />
        <div className="absolute inset-0 rounded-full border-4 border-transparent border-t-blue-500 animate-spin" />
        <div className="absolute inset-4 rounded-full bg-blue-50 animate-pulse" />
      </div>

      <h1 className="text-lg sm:text-xl font-semibold text-gray-800">
        Trwa weryfikacja materiału
      </h1>

      <p
        key={step}
        className="text-sm text-gray-500 mt-3 min-h-5 animate-pulse"
      >
        {STEPS[step]}
      </p>

      <div className="flex gap-1.5 mt-6" aria-hidden="true">
        {STEPS.map((_, i) => (
          <span
            key={i}
            className={`h-1.5 w-8 rounded-full transition-colors duration-500 ${
              i <= step ? 'bg-blue-500' : 'bg-gray-200'
            }`}
          />
        ))}
      </div>

      <p className="text-xs text-gray-400 mt-6">
        {seconds} s · zwykle trwa to do minuty
      </p>
    </div>
  );
};
