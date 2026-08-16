const path=location.pathname;if(path.startsWith('/v/'))publicView(path.split('/v/')[1]);else if(token())adminShell('dashboard');else loginView();
