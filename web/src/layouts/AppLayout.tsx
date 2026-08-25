import { Outlet } from "react-router-dom";
import AppSideBar from "@/components/layout/AppSidebar";

const AppLayout = () => {
  return (
    <div className="min-h-screen flex">
      <AppSideBar />
      <div className="flex flex-col flex-1">
        {/* Add Navbar later */}
        <main className="flex-1 p-6">
          <Outlet />
        </main>
      </div>
    </div>
  );
};

export default AppLayout;
