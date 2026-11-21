import { CommonFooter, LandingSearch, LandingTitle } from '../components';

export const LandingPage = () => {
  return (
    <main className="min-h-screen bg-white flex flex-col items-center px-5">
      <div className="w-full max-w-2xl flex flex-col items-center mt-[16vh] sm:mt-[20vh]">
        <LandingTitle />
        <LandingSearch />
      </div>
      <CommonFooter />
    </main>
  );
};
