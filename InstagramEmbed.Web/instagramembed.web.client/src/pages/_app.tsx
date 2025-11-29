import { Sidebar, SidebarProvider } from "@/components/ui/sidebar";
import { H1, Muted } from "@/components/ui/typography";
import { Outlet } from "react-router-dom";

function App() {
	return (
		<>
			<div className="flex flex-col h-full">
				<div className="flex h-full">
					<SidebarProvider className="h-full">
						<Sidebar></Sidebar>
					</SidebarProvider>
				</div>
				<div className="mt-auto">
					<div className="w-full bg-black text-white p-8">
						<div className="container mx-auto">
							<H1>Yaya</H1>
							<Muted>Meow</Muted>
						</div>
					</div>
				</div>
			</div>
		</>
	);
}

export default App;
