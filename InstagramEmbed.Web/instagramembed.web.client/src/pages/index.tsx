import { useEffect, useState } from "react";

import type { Session } from "../api";
import { api } from "../lib/utils";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "../components/ui/table";

function Home() {
	const [sessions, setSessions] = useState<Session[]>([]);

	useEffect(() => {
		api.adminGetSessionsGet().then((val) => {
			if (val.data != null) setSessions(val.data);
		});
	}, []);

	return (
		<div className="container mx-auto">
			<Table>
				<TableHeader>
					<TableRow>
						<TableHead>Session ID</TableHead>
						<TableHead>CSRF Token</TableHead>
						<TableHead>ExpiresOn</TableHead>
					</TableRow>
				</TableHeader>

				<TableBody>
					{sessions.map((item) => (
						<TableRow>
							<TableCell>{item.id}</TableCell>
							<TableCell>{item.csrfToken}</TableCell>
							<TableCell>{item.expiresOn?.toUTCString()}</TableCell>
						</TableRow>
					))}
				</TableBody>
			</Table>
		</div>
	);
}

export default Home;
