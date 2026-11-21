export const LandingTitle = () => {
  return (
    <>
      {/* Logo */}
      <h1 className="text-4xl sm:text-6xl font-bold tracking-tight mb-4 select-none font-montserrat">
        {'AntyManipulator'.split('').map((letter, index) => {
          const colors = [
            '#4285F4',
            '#EA4335',
            '#FBBC05',
            '#4285F4',
            '#34A853',
            '#EA4335',
          ];

          return (
            <span
              key={index}
              style={{
                color: colors[index % colors.length],
              }}
            >
              {letter}
            </span>
          );
        })}
      </h1>

      <p className="text-sm text-gray-500 mb-9 text-center">
        Sprawdź, zanim uwierzysz.
      </p>
    </>
  );
};
