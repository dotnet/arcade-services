import { Component, Input, OnInit, OnDestroy } from "@angular/core";
import { NavigationEnd, Router, ActivatedRoute } from "@angular/router";
import { Observable, SubscriptionLike, of } from "rxjs";
import { filter, map, switchMap, concat } from "rxjs/operators";

import { prettyRepository } from "../../util/names";
import { MaestroService } from 'src/maestro-client';
import { Channel, DefaultChannel } from 'src/maestro-client/models';

@Component({
  selector: "mc-side-bar-channel",
  templateUrl: "./side-bar-channel.component.html",
  styleUrls: ["./side-bar-channel.component.scss"],
})
export class SideBarChannelComponent implements OnInit, OnDestroy {

  public constructor(private maestro: MaestroService, private router: Router) { }
  @Input() public channel!: Channel;

  public isCollapsed = true;

  public branches$!: Observable<DefaultChannel[]>;

  public trimName = prettyRepository;

  private routeSubscription?: SubscriptionLike;

  public ngOnInit() {
    this.branches$ = this.maestro.defaultChannels.listAsync(undefined, this.channel.id);

    // Get the current route state, and concat it with any changes
    this.routeSubscription =
      of(this.router.routerState.root).pipe(
        concat(this.router.events.pipe(
          filter(evt => evt instanceof NavigationEnd),
          map(() => this.router.routerState.root),
        )),
        map(route => {
          while (route.firstChild) route = route.firstChild;
          return route;
        }),
        switchMap(r => r.paramMap),
      ).subscribe(params => {
        const channelIdStr = params.get("channelId")
        if (channelIdStr) {
          const channelId = +channelIdStr;
          this.isCollapsed = this.channel.id !== +channelId;
        }
      });
  }

  public ngOnDestroy() {
    if (this.routeSubscription) {
      this.routeSubscription.unsubscribe();
    }
  }
}
