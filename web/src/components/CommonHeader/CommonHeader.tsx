import { Link } from 'react-router';

export const CommonHeader = () => {
  return (
    <footer className="mb-auto py-4 text-gray-400">
      <Link to="/" className="text-gray-950">
        AntyManipulator
      </Link>
    </footer>
  );
};
