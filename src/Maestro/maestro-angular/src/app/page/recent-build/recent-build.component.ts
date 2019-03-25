import { Component, OnInit } from '@angular/core';
import { Router, ActivatedRoute } from '@angular/router';
import { MaestroService } from 'src/maestro-client';
import { Build, Channel } from 'src/maestro-client/models';
import { Observable, of } from 'rxjs';
import { map, switchMap, } from 'rxjs/operators';
import { statefulSwitchMap, statefulPipe, StatefulResult } from 'src/stateful';
import { ChannelService } from 'src/app/services/channel.service';

@Component({
  selector: 'mc-recent-build',
  templateUrl: './recent-build.component.html',
  styleUrls: ['./recent-build.component.scss']
})
export class RecentBuildComponent implements OnInit {

  constructor(private maestro: MaestroService, private channel: ChannelService, private router: Router, private route: ActivatedRoute) { }

  public navigation$!: Observable<StatefulResult<boolean>>;

  public channelId?: string;
  public repository?: string;

  public channel$?: Observable<Channel>;

  ngOnInit() {
    this.navigation$ = this.route.paramMap.pipe(
      statefulSwitchMap(params => {
        const channelId = params.get("channelId");
        const repository = params.get("repository");
        if (channelId == null) {
          throw new Error("channelId was null");
        }
        if (repository == null) {
          throw new Error("repository was null");
        }
        this.channelId = channelId;
        this.repository = repository;
        this.channel$ = this.channel.getChannels().pipe(
          map(channels => channels.find(c => c.id === +channelId) as Channel),
        );

        return this.maestro.builds.getLatestAsync({
          channelId: +channelId,
          repository: repository,
        }).pipe(
          map(b => [+channelId, repository, b] as [number, string, Build]),
        );
      }),
      statefulPipe(
        switchMap(([channelId, repository, build]) => {
          return this.router.navigate(["/", channelId, repository, build.id], { replaceUrl: true });
        }),
      ),
    );
  }

}
